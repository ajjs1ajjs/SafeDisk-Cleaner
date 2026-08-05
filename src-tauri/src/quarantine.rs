use crate::models::QuarantineEntry;
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Serialize, Deserialize)]
struct Manifest {
    id: String,
    original_path: String,
    quarantined_at: String,
    expires_at: String,
    size: u64,
}

fn now_days_epoch() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs()
        / 86400
}

fn date_from_epoch_days(days: u64) -> String {
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

fn now_string() -> String {
    date_from_epoch_days(now_days_epoch())
}

pub fn quarantine_file(path: &Path, original_path: &str, retention_days: u64) -> Result<String, String> {
    let root = crate::paths::quarantine_dir();
    let id = uuid::Uuid::new_v4().to_string();
    let target = root.join(&id);
    std::fs::create_dir_all(&target).map_err(|e| e.to_string())?;

    let name = path
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_else(|| "file".into());
    let dest = target.join(name);

    let size = std::fs::metadata(path).map(|m| m.len()).unwrap_or(0);

    if std::fs::rename(path, &dest).is_err() {
        std::fs::copy(path, &dest).map_err(|e| format!("quarantine copy failed: {}", e))?;
        std::fs::remove_file(path).map_err(|e| format!("quarantine remove failed: {}", e))?;
    }

    let manifest = Manifest {
        id: id.clone(),
        original_path: original_path.to_string(),
        quarantined_at: now_string(),
        expires_at: date_from_epoch_days(now_days_epoch() + retention_days),
        size,
    };

    let m = serde_json::to_string_pretty(&manifest).map_err(|e| e.to_string())?;
    std::fs::write(target.join("manifest.json"), m).map_err(|e| e.to_string())?;

    Ok(id)
}

pub fn list_quarantine() -> Vec<QuarantineEntry> {
    let root = crate::paths::quarantine_dir();
    let mut out = Vec::new();
    let entries = match std::fs::read_dir(&root) {
        Ok(e) => e,
        Err(_) => return out,
    };
    for entry in entries.flatten() {
        let manifest_path = entry.path().join("manifest.json");
        if !manifest_path.exists() {
            continue;
        }
        if let Ok(raw) = std::fs::read_to_string(&manifest_path) {
            if let Ok(m) = serde_json::from_str::<Manifest>(&raw) {
                out.push(QuarantineEntry {
                    id: m.id,
                    original_path: m.original_path,
                    quarantined_path: entry
                        .path()
                        .file_name()
                        .map(|n| n.to_string_lossy().to_string())
                        .unwrap_or_default(),
                    size: m.size,
                    quarantined_at: m.quarantined_at,
                    expires_at: m.expires_at,
                });
            }
        }
    }
    out.sort_by(|a, b| b.quarantined_at.cmp(&a.quarantined_at));
    out
}

fn manifest_for(id: &str) -> Option<(PathBuf, Manifest)> {
    let root = crate::paths::quarantine_dir();
    let dir = root.join(id);
    let manifest_path = dir.join("manifest.json");
    if !manifest_path.exists() {
        return None;
    }
    let raw = std::fs::read_to_string(&manifest_path).ok()?;
    let m = serde_json::from_str::<Manifest>(&raw).ok()?;
    Some((dir, m))
}

pub fn restore_quarantine(id: &str) -> Result<(), String> {
    let (dir, m) = manifest_for(id).ok_or_else(|| "Quarantine entry not found".to_string())?;
    let original = PathBuf::from(&m.original_path);
    if let Some(parent) = original.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    let stored = dir
        .read_dir()
        .map_err(|e| e.to_string())?
        .flatten()
        .map(|e| e.path())
        .find(|p| p.file_name().map(|n| n != "manifest.json").unwrap_or(false));
    let stored = stored.ok_or_else(|| "Stored file not found".to_string())?;

    if original.exists() {
        return Err("Target path already exists; restore refused".to_string());
    }
    std::fs::rename(&stored, &original).map_err(|e| e.to_string())?;
    std::fs::remove_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(())
}

pub fn remove_quarantine(id: &str) -> Result<(), String> {
    let root = crate::paths::quarantine_dir();
    let dir = root.join(id);
    if dir.exists() {
        std::fs::remove_dir_all(&dir).map_err(|e| e.to_string())?;
    }
    Ok(())
}

pub fn empty_quarantine() -> Result<usize, String> {
    let root = crate::paths::quarantine_dir();
    let entries = list_quarantine();
    for e in &entries {
        let dir = root.join(&e.id);
        if dir.exists() {
            let _ = std::fs::remove_dir_all(&dir);
        }
    }
    Ok(entries.len())
}

pub fn purge_expired(retention_days: u64) -> usize {
    let mut purged = 0;
    let entries = list_quarantine();
    let cutoff = now_days() as i64 - retention_days as i64;
    for e in entries {
        let day = parse_day(&e.quarantined_at);
        if day <= cutoff {
            if remove_quarantine(&e.id).is_ok() {
                purged += 1;
            }
        }
    }
    purged
}

fn now_days() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs()
        / 86400
}

fn parse_day(s: &str) -> i64 {
    let y = s.get(0..4).and_then(|x| x.parse::<i64>().ok()).unwrap_or(0);
    let m = s.get(5..7).and_then(|x| x.parse::<i64>().ok()).unwrap_or(0);
    let d = s.get(8..10).and_then(|x| x.parse::<i64>().ok()).unwrap_or(0);
    if y == 0 {
        return i64::MIN;
    }
    let yy = y - if m <= 2 { 1 } else { 0 };
    let era = if yy >= 0 { yy } else { yy - 399 } / 400;
    let yoe = yy - era * 400;
    let mp = if m > 2 { m - 3 } else { m + 9 };
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146097 + doe - 719468
}
