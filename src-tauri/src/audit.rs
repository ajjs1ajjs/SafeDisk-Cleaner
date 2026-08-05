use crate::models::AuditEntry;
use std::fs::OpenOptions;
use std::io::Write;

const MAX_LOG_BYTES: u64 = 5 * 1024 * 1024;

fn audit_file() -> std::path::PathBuf {
    crate::paths::audit_dir().join("audit.log.jsonl")
}

pub fn append(entry: &AuditEntry) {
    let path = audit_file();
    if let Ok(meta) = std::fs::metadata(&path) {
        if meta.len() > MAX_LOG_BYTES {
            let _ = std::fs::rename(&path, crate::paths::audit_dir().join("audit.log.jsonl.old"));
        }
    }
    let line = match serde_json::to_string(entry) {
        Ok(l) => l,
        Err(_) => return,
    };
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(&path) {
        let _ = writeln!(f, "{}", line);
    }
}

pub fn read_all() -> Vec<AuditEntry> {
    let mut out = Vec::new();
    let old = crate::paths::audit_dir().join("audit.log.jsonl.old");
    for p in [old, audit_file()] {
        if let Ok(content) = std::fs::read_to_string(&p) {
            for line in content.lines() {
                if let Ok(e) = serde_json::from_str::<AuditEntry>(line) {
                    out.push(e);
                }
            }
        }
    }
    out
}

pub fn clear() {
    let _ = std::fs::remove_file(audit_file());
    let _ = std::fs::remove_file(crate::paths::audit_dir().join("audit.log.jsonl.old"));
}
