use crate::confidence::{action_for, compute, ConfidenceInput};
use crate::models::*;
use crate::rules::{classify, Match};
use crate::windows_utils;
use rayon::prelude::*;
use std::os::windows::fs::MetadataExt;
use std::path::{Path, PathBuf};
use std::sync::mpsc;
use std::time::{Instant, SystemTime, UNIX_EPOCH};
use walkdir::WalkDir;

fn env_path(name: &str) -> Option<String> {
    std::env::var_os(name)
        .map(|v| PathBuf::from(v).to_string_lossy().to_string())
        .filter(|p| !p.is_empty())
}

pub fn default_scan_roots(include_medium: bool, include_advanced: bool) -> Vec<String> {
    let mut roots: Vec<String> = Vec::new();

    if let Some(t) = env_path("TEMP") {
        roots.push(t);
    }
    if let Some(t) = env_path("TMP") {
        if !roots.contains(&t) {
            roots.push(t);
        }
    }
    let local = env_path("LOCALAPPDATA");
    if let Some(l) = &local {
        roots.push(format!(r"{}\CrashDumps", l));
        roots.push(format!(r"{}\Google\Chrome\User Data\Default\Cache", l));
        roots.push(format!(r"{}\Google\Chrome\User Data\Default\Code Cache", l));
        roots.push(format!(r"{}\Google\Chrome\User Data\Crashpad\reports", l));
        roots.push(format!(r"{}\Microsoft\Edge\User Data\Default\Cache", l));
        roots.push(format!(r"{}\Microsoft\Edge\User Data\Default\Code Cache", l));
        roots.push(format!(r"{}\Microsoft\Edge\User Data\Crashpad\reports", l));
        roots.push(format!(r"{}\Microsoft\Windows\Explorer", l));
        roots.push(format!(r"{}\NuGet\Cache", l));
        roots.push(format!(r"{}\npm-cache", l));
        roots.push(format!(r"{}\pip\cache", l));
        roots.push(format!(r"{}\Mozilla\Firefox\Profiles", l));
    }

    let windows = env_path("WINDIR");
    if let Some(w) = &windows {
        roots.push(format!(r"{}\Temp", w));
        if include_medium {
            roots.push(format!(r"{}\SoftwareDistribution\Download", w));
            roots.push(format!(r"{}\DriverStore", w));
        }
    }

    if include_advanced {
        for old in [r"C:\Windows.old", r"C:\Windows~old"] {
            if Path::new(old).is_dir() {
                roots.push(old.to_string());
            }
        }
    }

    let mut filtered: Vec<String> = Vec::new();
    for r in roots {
        let p = PathBuf::from(&r);
        if p.is_dir() && !filtered.contains(&r) {
            filtered.push(r);
        }
    }
    filtered
}

fn fmt_date(t: SystemTime) -> Option<String> {
    let secs = t.duration_since(UNIX_EPOCH).ok()?.as_secs();
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
    Some(format!("{:04}-{:02}-{:02}", y, m, d))
}

fn process_file(path: &Path, opts: &ScanOptions) -> Option<Candidate> {
    let meta = std::fs::metadata(path).ok()?;
    if !meta.is_file() {
        return None;
    }
    let size = meta.len();
    if size == 0 {
        return None;
    }

    let attrs = std::fs::metadata(path).ok().map(|m| m.file_attributes());

    let match_result = classify(path);
    let (category, base, reason) = match match_result {
        Match::Candidate {
            category,
            base_confidence,
            reason,
        } => (category, base_confidence, reason),
        Match::Protected | Match::None => return None,
    };

    if !opts.include_medium && matches!(category.risk_level(), RiskLevel::Medium | RiskLevel::Advanced) {
        return None;
    }
    if !opts.include_advanced && matches!(category.risk_level(), RiskLevel::Advanced) {
        return None;
    }

    let modified = meta.modified().ok();
    let accessed = meta.accessed().ok();

    let locked = base >= 80 && windows_utils::is_locked(path);
    let system_attr = attrs.map(windows_utils::is_system_attr).unwrap_or(false);

    let confidence = compute(ConfidenceInput {
        base,
        category,
        size,
        last_access: accessed,
        recency_days: opts.recency_days,
        locked,
        system_attr,
    });

    if confidence < opts.min_confidence {
        return None;
    }

    let risk = category.risk_level();
    let action = action_for(confidence, risk);
    let last_access_days = accessed.and_then(|a| crate::confidence::elapsed_days(a));

    Some(Candidate {
        path: path.to_string_lossy().to_string(),
        size,
        category,
        confidence,
        action,
        reason,
        last_modified: modified.and_then(fmt_date),
        last_access_days,
        risk_level: risk,
    })
}

fn should_prune(entry: &walkdir::DirEntry) -> bool {
    if !entry.file_type().is_dir() {
        return false;
    }
    let p = entry.path();
    if crate::models::is_protected_path(p) {
        return true;
    }
    let lower = p.to_string_lossy().to_lowercase();
    lower.contains(r"\recovery\")
        || lower.contains(r"\system volume information")
        || lower.contains(r"$recycle.bin")
}

fn scan_one_root(
    root: &str,
    opts: &ScanOptions,
    tx: &mpsc::Sender<ScanProgress>,
    root_idx: usize,
    total_roots: usize,
) -> (Vec<Candidate>, u64, u64) {
    let mut candidates = Vec::new();
    let mut files: u64 = 0;
    let mut dirs: u64 = 0;

    for entry in WalkDir::new(root)
        .follow_links(false)
        .into_iter()
        .filter_entry(|e| !should_prune(e))
    {
        let entry = match entry {
            Ok(e) => e,
            Err(_) => continue,
        };
        if entry.file_type().is_dir() {
            dirs += 1;
            continue;
        }
        if entry.file_type().is_file() {
            files += 1;
            if let Some(c) = process_file(entry.path(), opts) {
                candidates.push(c);
            }
            if files % 200 == 0 {
                let partial = (files % 2000) as f64 / 2000.0;
                let _ = tx.send(ScanProgress {
                    current_root: root.to_string(),
                    files_scanned: files,
                    dirs_scanned: dirs,
                    candidates_found: candidates.len() as u64,
                    percent: ((root_idx as f64 + partial) / total_roots as f64) * 100.0,
                    finished: false,
                });
            }
        }
    }
    (candidates, files, dirs)
}

fn special_candidates(opts: &ScanOptions) -> Vec<Candidate> {
    let mut out = Vec::new();

    if let Some(info) = windows_utils::query_recycle_bin(None) {
        if info.size > 0 || info.count > 0 {
            let confidence = 99u8;
            if confidence >= opts.min_confidence {
                out.push(Candidate {
                    path: "__recycle_bin__".into(),
                    size: info.size,
                    category: Category::RecycleBin,
                    confidence,
                    action: CandidateAction::Delete,
                    reason: format!("Recycle Bin contains {} items", info.count),
                    last_modified: None,
                    last_access_days: None,
                    risk_level: RiskLevel::Safe,
                });
            }
        }
    }

    let memdmp = Path::new(r"C:\MEMORY.DMP");
    if memdmp.exists() {
        if let Some(mut c) = process_file(memdmp, opts) {
            c.category = Category::CrashDump;
            c.confidence = c.confidence.max(95);
            c.reason = "System memory dump at drive root".into();
            c.risk_level = RiskLevel::Safe;
            out.push(c);
        }
    }

    out
}

pub fn scan(opts: &ScanOptions) -> ScanResult {
    scan_with_progress(opts, |_| {})
}

pub fn scan_with_progress(
    opts: &ScanOptions,
    mut on_progress: impl FnMut(ScanProgress),
) -> ScanResult {
    let started = Instant::now();
    let roots = if opts.roots.is_empty() {
        default_scan_roots(opts.include_medium, opts.include_advanced)
    } else {
        opts.roots.clone()
    };
    let total_roots = roots.len().max(1);

    let (tx, rx) = mpsc::channel();
    let results: Vec<(Vec<Candidate>, u64, u64)> = roots
        .par_iter()
        .enumerate()
        .map(|(i, r)| scan_one_root(r, opts, &tx, i, total_roots))
        .collect();
    drop(tx);

    for p in rx {
        on_progress(p);
    }

    let mut candidates: Vec<Candidate> = results.iter().flat_map(|r| r.0.iter().cloned()).collect();
    candidates.extend(special_candidates(opts));

    let scanned_files: u64 = results.iter().map(|r| r.1).sum();
    let scanned_dirs: u64 = results.iter().map(|r| r.2).sum();

    let mut cat_stats: std::collections::HashMap<Category, (usize, u64, u64)> =
        std::collections::HashMap::new();
    for c in &candidates {
        let e = cat_stats.entry(c.category).or_insert((0, 0, 0));
        e.0 += 1;
        e.1 += c.size;
        if c.action == CandidateAction::Delete || c.action == CandidateAction::Review {
            e.2 += c.size;
        }
    }

    let mut categories: Vec<CategoryStats> = cat_stats
        .into_iter()
        .map(|(category, (count, size, potential))| CategoryStats {
            category,
            risk_level: category.risk_level(),
            count,
            size,
            potential,
        })
        .collect();
    categories.sort_by_key(|c| std::cmp::Reverse(c.potential));

    candidates.sort_by(|a, b| b.confidence.cmp(&a.confidence).then(b.size.cmp(&a.size)));

    let total_potential = candidates
        .iter()
        .filter(|c| c.action == CandidateAction::Delete || c.action == CandidateAction::Review)
        .map(|c| c.size)
        .sum();

    on_progress(ScanProgress {
        current_root: String::new(),
        files_scanned: scanned_files,
        dirs_scanned: scanned_dirs,
        candidates_found: candidates.len() as u64,
        percent: 100.0,
        finished: true,
    });

    ScanResult {
        candidates,
        summary: ScanSummary {
            scanned_dirs,
            scanned_files,
            elapsed_ms: started.elapsed().as_millis(),
            total_potential,
            total_candidates: 0,
            categories,
        },
    }
}

pub fn scan_duplicates(roots: Vec<String>) -> ScanResult {
    let mut size_map: std::collections::HashMap<u64, Vec<PathBuf>> = std::collections::HashMap::new();
    for root in roots {
        for entry in WalkDir::new(root)
            .follow_links(false)
            .into_iter()
            .filter_entry(|e| !should_prune(e))
        {
            let entry = match entry {
                Ok(e) => e,
                Err(_) => continue,
            };
            if !entry.file_type().is_file() {
                continue;
            }
            let meta = match entry.metadata() {
                Ok(m) => m,
                Err(_) => continue,
            };
            if meta.len() < 4096 {
                continue;
            }
            size_map.entry(meta.len()).or_default().push(entry.path().to_path_buf());
        }
    }

    let mut candidates = Vec::new();
    for (_size, group) in size_map.into_iter() {
        if group.len() < 2 {
            continue;
        }
        let mut hashes: Vec<(PathBuf, [u8; 32])> = Vec::new();
        for p in group {
            if let Ok(h) = hash_file(&p) {
                hashes.push((p, h));
            }
        }
        let mut seen: std::collections::HashMap<[u8; 32], PathBuf> = std::collections::HashMap::new();
        for (p, h) in hashes {
            if let Some(first) = seen.get(&h) {
                let meta = std::fs::metadata(&p).ok();
                if let Some(m) = meta {
                    candidates.push(Candidate {
                        path: p.to_string_lossy().to_string(),
                        size: m.len(),
                        category: Category::DuplicateFiles,
                        confidence: 98,
                        action: CandidateAction::Review,
                        reason: format!("Duplicate of {}", first.to_string_lossy()),
                        last_modified: m.modified().ok().and_then(fmt_date),
                        last_access_days: m.accessed().ok().and_then(crate::confidence::elapsed_days),
                        risk_level: RiskLevel::Advanced,
                    });
                }
            } else {
                seen.insert(h, p);
            }
        }
    }

    candidates.sort_by(|a, b| b.size.cmp(&a.size));
    let total_potential = candidates.iter().map(|c| c.size).sum();
    ScanResult {
        candidates,
        summary: ScanSummary {
            scanned_dirs: 0,
            scanned_files: 0,
            elapsed_ms: 0,
            total_potential,
            total_candidates: 0,
            categories: Vec::new(),
        },
    }
}

pub fn hash_file(path: &Path) -> Result<[u8; 32], std::io::Error> {
    use std::io::Read;
    let mut file = std::fs::File::open(path)?;
    let mut hasher = blake3::Hasher::new();
    let mut buf = vec![0u8; 1024 * 1024];
    loop {
        let n = file.read(&mut buf)?;
        if n == 0 {
            break;
        }
        hasher.update(&buf[..n]);
    }
    Ok(*hasher.finalize().as_bytes())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn test_root(label: &str) -> PathBuf {
        std::env::temp_dir().join(format!("safedisk-test-scan-{}", label))
    }

    fn setup_dir(label: &str) -> PathBuf {
        let root = test_root(label);
        std::fs::remove_dir_all(&root).ok();
        std::fs::create_dir_all(root.join("cache")).unwrap();
        std::fs::create_dir_all(root.join("logs")).unwrap();
        std::fs::write(root.join("cache").join("f_0001"), vec![0u8; 2048]).unwrap();
        std::fs::write(root.join("cache").join("f_0002"), vec![0u8; 4096]).unwrap();
        std::fs::write(root.join("logs").join("app.log"), b"log line\n").unwrap();
        std::fs::write(root.join("tool.exe"), vec![0u8; 8192]).unwrap();
        std::fs::write(root.join("notes.txt"), b"keep me").unwrap();
        root
    }

    #[test]
    fn scan_finds_candidates_and_skips_protected() {
        let root = setup_dir("find");
        let opts = ScanOptions {
            roots: vec![root.to_string_lossy().to_string()],
            ..Default::default()
        };
        let result = scan(&opts);

        let local: Vec<&Candidate> = result
            .candidates
            .iter()
            .filter(|c| c.path.contains("safedisk-test-scan-find"))
            .collect();

        let has_txt_candidate = local.iter().any(|c| c.path.ends_with("notes.txt"));
        let has_exe = local.iter().any(|c| c.path.ends_with("tool.exe"));
        let has_cache = local.iter().any(|c| c.path.contains("cache"));
        let has_log = local.iter().any(|c| c.path.ends_with("app.log"));

        assert!(has_txt_candidate, "notes.txt should be a temp candidate");
        assert!(has_cache, "cache files should be candidates");
        assert!(has_log, "app.log should be a log candidate");
        assert!(!has_exe, "protected .exe must never be a candidate");
        assert!(result.summary.scanned_files >= 4);
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn scan_respects_min_confidence() {
        let root = setup_dir("conf");
        let low = scan(&ScanOptions {
            roots: vec![root.to_string_lossy().to_string()],
            min_confidence: 50,
            ..Default::default()
        });
        let high = scan(&ScanOptions {
            roots: vec![root.to_string_lossy().to_string()],
            min_confidence: 100,
            ..Default::default()
        });

        let low_local = low
            .candidates
            .iter()
            .filter(|c| c.path.contains("safedisk-test-scan-conf"))
            .count();
        let high_local = high
            .candidates
            .iter()
            .filter(|c| c.path.contains("safedisk-test-scan-conf"))
            .count();

        assert!(
            low_local >= high_local,
            "lower threshold must find at least as many candidates"
        );
        assert!(
            high.candidates
                .iter()
                .all(|c| c.confidence >= 100),
            "no candidate may fall below min_confidence"
        );
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn classify_result_carries_reason() {
        let root = setup_dir("reason");
        let opts = ScanOptions {
            roots: vec![root.to_string_lossy().to_string()],
            ..Default::default()
        };
        let result = scan(&opts);
        let local: Vec<&Candidate> = result
            .candidates
            .iter()
            .filter(|c| c.path.contains("safedisk-test-scan-reason"))
            .collect();
        assert!(local.iter().all(|c| !c.reason.is_empty()));
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn scan_duplicates_detects_identical_files() {
        let root = test_root("dup");
        std::fs::remove_dir_all(&root).ok();
        std::fs::create_dir_all(&root).unwrap();
        let a = root.join("a.bin");
        let b = root.join("b.bin");
        let c = root.join("c.bin");
        let data = vec![0xAB; 5000];
        std::fs::write(&a, &data).unwrap();
        std::fs::write(&b, &data).unwrap();
        std::fs::write(&c, b"different").unwrap();
        let result = scan_duplicates(vec![root.to_string_lossy().to_string()]);
        assert_eq!(result.candidates.len(), 1, "only one duplicate among two identical files");
        assert_eq!(result.candidates[0].category, Category::DuplicateFiles);
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn hash_file_is_deterministic() {
        let dir = std::env::temp_dir().join("safedisk-test-hash");
        std::fs::create_dir_all(&dir).unwrap();
        let f = dir.join("x.bin");
        std::fs::write(&f, vec![7u8; 100_000]).unwrap();
        assert_eq!(hash_file(&f).unwrap(), hash_file(&f).unwrap());
        std::fs::remove_dir_all(&dir).ok();
    }
}
