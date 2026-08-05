use crate::models::{Category, RiskLevel};
use crate::rules::is_protected_extension;
use crate::windows_utils;
use std::path::Path;

pub struct SafetyVerdict {
    pub allowed: bool,
    pub reasons: Vec<String>,
}

impl SafetyVerdict {
    pub fn deny(reason: String) -> Self {
        Self {
            allowed: false,
            reasons: vec![reason],
        }
    }
}

pub fn validate(path: &Path, category: Category, recency_days: u64) -> SafetyVerdict {
    if path.to_string_lossy().is_empty() || path.file_name().is_none() {
        return SafetyVerdict::deny("Invalid path".into());
    }

    if is_protected_extension(path) {
        return SafetyVerdict::deny(format!(
            "Protected extension: .{}",
            path.extension().unwrap_or_default().to_string_lossy()
        ));
    }

    if crate::models::is_protected_path(path) {
        return SafetyVerdict::deny("Path belongs to a protected system directory".into());
    }

    let lower = path.to_string_lossy().to_lowercase();
    if lower.contains(r"\safedisk\quarantine") || lower.contains("__recycle_bin__") {
        return SafetyVerdict::deny("Path is part of SafeDisk internals".into());
    }

    if let Some(attrs) = windows_utils::metadata_attrs(path) {
        if windows_utils::is_system_attr(attrs) {
            return SafetyVerdict::deny("File has the SYSTEM attribute".into());
        }
    }

    if let Ok(meta) = std::fs::metadata(path) {
        if let Ok(accessed) = meta.accessed() {
            if let Some(days) = crate::confidence::elapsed_days(accessed) {
                if days < recency_days {
                    return SafetyVerdict::deny(format!(
                        "File was accessed {} day(s) ago (recency threshold {} days)",
                        days, recency_days
                    ));
                }
            }
        }
    }

    if windows_utils::is_locked(path) {
        return SafetyVerdict::deny("File is open by another process".into());
    }

    if matches!(category.risk_level(), RiskLevel::Advanced) {
        if windows_utils::has_microsoft_signature(path) {
            return SafetyVerdict::deny("File carries a Microsoft digital signature".into());
        }
    }

    SafetyVerdict {
        allowed: true,
        reasons: Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_path_is_denied() {
        let v = validate(Path::new(""), Category::Temp, 0);
        assert!(!v.allowed);
    }

    #[test]
    fn path_without_filename_is_denied() {
        let v = validate(Path::new(r"C:\"), Category::Temp, 0);
        assert!(!v.allowed);
    }

    #[test]
    fn protected_extension_is_denied() {
        let v = validate(Path::new(r"C:\Temp\foo.dll"), Category::Temp, 0);
        assert!(!v.allowed);
        assert!(v.reasons[0].contains("Protected extension"));
    }

    #[test]
    fn protected_system_path_is_denied() {
        for path in [
            r"C:\Windows\Temp\foo.txt",
            r"C:\Windows\System32\foo.txt",
            r"C:\Program Files\foo\foo.txt",
            r"C:\ProgramData\foo\foo.txt",
        ] {
            let v = validate(Path::new(path), Category::Temp, 0);
            assert!(!v.allowed, "expected deny for {}", path);
        }
    }

    #[test]
    fn safedisk_internal_path_is_denied() {
        let v = validate(
            Path::new(r"C:\Users\user\AppData\Local\SafeDisk\Quarantine\abc\file.txt"),
            Category::Temp,
            0,
        );
        assert!(!v.allowed);
        assert!(v.reasons[0].contains("SafeDisk"));
    }

    #[test]
    fn recycle_bin_sentinel_is_denied() {
        let v = validate(Path::new("__recycle_bin__"), Category::RecycleBin, 0);
        assert!(!v.allowed);
    }

    #[test]
    fn fresh_file_is_denied_by_recency() {
        let dir = std::env::temp_dir().join("safedisk-test-safety");
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("fresh.txt");
        std::fs::write(&file, b"hello").unwrap();
        let v = validate(&file, Category::Temp, 1_000_000);
        std::fs::remove_dir_all(&dir).ok();
        assert!(!v.allowed);
        assert!(v.reasons[0].contains("accessed"));
    }

    #[test]
    fn normal_file_is_allowed() {
        let dir = std::env::temp_dir().join("safedisk-test-safety");
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("normal.txt");
        std::fs::write(&file, b"hello").unwrap();
        let v = validate(&file, Category::Temp, 0);
        std::fs::remove_dir_all(&dir).ok();
        assert!(v.allowed, "reasons: {:?}", v.reasons);
    }

    #[test]
    fn advanced_category_unsigned_file_is_allowed() {
        let dir = std::env::temp_dir().join("safedisk-test-safety");
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("unsigned.txt");
        std::fs::write(&file, b"not signed").unwrap();
        let v = validate(&file, Category::DuplicateFiles, 0);
        std::fs::remove_dir_all(&dir).ok();
        assert!(
            v.allowed,
            "an unsigned file must not be blocked by the signature check: {:?}",
            v.reasons
        );
    }
}
