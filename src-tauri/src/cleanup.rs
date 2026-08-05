use crate::models::*;
use crate::paths;
use crate::safety;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

const LARGE_FILE_THRESHOLD: u64 = 64 * 1024 * 1024;

fn today() -> String {
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let days = secs / 86400;
    let civil = (days as i64) + 719468;
    let era = if civil >= 0 { civil } else { civil - 146096 } / 146097;
    let doe = civil - era * 146097;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };
    format!("{:04}-{:02}-{:02}", y, m, d)
}

pub fn run(candidates: &[Candidate], opts: &CleanupOptions) -> CleanupResult {
    let _ = crate::quarantine::purge_expired(opts.quarantine_retention_days);

    let mut ordered: Vec<&Candidate> = candidates.iter().collect();
    ordered.sort_by(|a, b| {
        b.confidence
            .cmp(&a.confidence)
            .then(b.size.cmp(&a.size))
    });

    let mut entries = Vec::new();
    let mut freed: u64 = 0;
    let mut processed: usize = 0;

    for cand in ordered {
        if cand.action == CandidateAction::Keep {
            continue;
        }
        processed += 1;

        if matches!(opts.mode, CleanMode::DryRun) {
            freed += cand.size;
            entries.push(CleanupEntry {
                path: cand.path.clone(),
                size: cand.size,
                category: cand.category,
                confidence: cand.confidence,
                status: CleanupStatus::WouldDelete,
                detail: "Dry run — nothing was deleted".into(),
            });
            continue;
        }

        let result = execute(cand, opts);
        match result {
            Ok(status) => {
                if matches!(status, CleanupStatus::Deleted | CleanupStatus::Quarantined | CleanupStatus::Recycled) {
                    freed += cand.size;
                }
                entries.push(CleanupEntry {
                    path: cand.path.clone(),
                    size: cand.size,
                    category: cand.category,
                    confidence: cand.confidence,
                    status: status.clone(),
                    detail: status_description(&status).to_string(),
                });
                let success = matches!(status, CleanupStatus::Deleted | CleanupStatus::Quarantined | CleanupStatus::Recycled);
                crate::audit::append(&AuditEntry {
                    date: today(),
                    action: status.as_str().into(),
                    path: cand.path.clone(),
                    size: cand.size,
                    success,
                    detail: status_description(&status).to_string(),
                });
            }
            Err(reason) => {
                entries.push(CleanupEntry {
                    path: cand.path.clone(),
                    size: cand.size,
                    category: cand.category,
                    confidence: cand.confidence,
                    status: CleanupStatus::Failed,
                    detail: reason.clone(),
                });
                crate::audit::append(&AuditEntry {
                    date: today(),
                    action: "failed".into(),
                    path: cand.path.clone(),
                    size: cand.size,
                    success: false,
                    detail: reason,
                });
            }
        }
    }

    CleanupResult {
        mode: opts.mode,
        processed,
        deleted: entries
            .iter()
            .filter(|e| matches!(e.status, CleanupStatus::Deleted | CleanupStatus::Quarantined | CleanupStatus::Recycled))
            .count(),
        freed_bytes: freed,
        entries,
    }
}

fn execute(cand: &Candidate, opts: &CleanupOptions) -> Result<CleanupStatus, String> {
    if matches!(opts.mode, CleanMode::Auto) {
        if cand.risk_level != RiskLevel::Safe {
            return Err("Auto mode skips non-Safe risk level (needs review)".into());
        }
        if cand.confidence < opts.auto_threshold {
            return Err(format!(
                "Confidence {} below auto threshold {}",
                cand.confidence, opts.auto_threshold
            ));
        }
    }

    let path = Path::new(&cand.path);

    if cand.category == Category::RecycleBin && cand.path == "__recycle_bin__" {
        let ok = crate::windows_utils::empty_recycle_bin();
        return if ok {
            Ok(CleanupStatus::Deleted)
        } else {
            Err("Failed to empty Recycle Bin".into())
        };
    }

    let verdict = safety::validate(path, cand.category, 3);
    if !verdict.allowed {
        return Err(verdict.reasons.join("; "));
    }

    let is_large = cand.size >= LARGE_FILE_THRESHOLD;

    if is_large || !opts.move_to_recycle_bin {
        let _id = crate::quarantine::quarantine_file(
            path,
            &cand.path,
            opts.quarantine_retention_days,
        )?;
        Ok(CleanupStatus::Quarantined)
    } else {
        match crate::windows_utils::move_to_recycle_bin(path) {
            Ok(()) => Ok(CleanupStatus::Recycled),
            Err(_e) => {
                let _ = crate::quarantine::quarantine_file(path, &cand.path, opts.quarantine_retention_days)?;
                Ok(CleanupStatus::Quarantined)
            }
        }
    }
}

fn status_description(status: &CleanupStatus) -> &'static str {
    match status {
        CleanupStatus::Deleted => "Deleted permanently",
        CleanupStatus::Quarantined => "Moved to SafeDisk quarantine",
        CleanupStatus::Recycled => "Moved to Recycle Bin",
        CleanupStatus::Skipped => "Skipped",
        CleanupStatus::Failed => "Failed",
        CleanupStatus::WouldDelete => "Would delete",
    }
}

pub fn write_report(result: &CleanupResult) -> Result<std::path::PathBuf, String> {
    let file = paths::reports_dir().join(format!("cleanup-{}.json", today()));
    let json = serde_json::to_string_pretty(result).map_err(|e| e.to_string())?;
    std::fs::write(&file, json).map_err(|e| e.to_string())?;
    Ok(file)
}

pub fn write_scan_report(result: &ScanResult) -> Result<std::path::PathBuf, String> {
    let file = paths::reports_dir().join(format!("scan-{}.json", today()));
    let json = serde_json::to_string_pretty(result).map_err(|e| e.to_string())?;
    std::fs::write(&file, json).map_err(|e| e.to_string())?;
    Ok(file)
}
