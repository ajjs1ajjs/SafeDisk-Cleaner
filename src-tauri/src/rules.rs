use crate::models::Category;
use std::path::Path;

const PROTECTED_EXTENSIONS: [&str; 7] = ["dll", "sys", "exe", "cat", "inf", "msi", "msp"];

pub fn is_protected_extension(path: &Path) -> bool {
    let ext = path
        .extension()
        .and_then(|e| e.to_str())
        .map(|e| e.to_lowercase());
    match ext {
        Some(e) => PROTECTED_EXTENSIONS.contains(&e.as_str()),
        None => false,
    }
}

fn contains_lowercase(path: &Path, needles: &[&str]) -> bool {
    let lower = path.to_string_lossy().to_lowercase();
    needles.iter().any(|n| lower.contains(n))
}

fn file_name_lower(path: &Path) -> String {
    path.file_name()
        .map(|n| n.to_string_lossy().to_lowercase())
        .unwrap_or_default()
}

pub enum Match {
    None,
    Candidate { category: Category, base_confidence: u8, reason: String },
    Protected,
}

pub fn classify(path: &Path) -> Match {
    if is_protected_extension(path) {
        return Match::Protected;
    }

    let lower = path.to_string_lossy().to_lowercase();
    let name = file_name_lower(path);

    if name == "memdmp.dmp" || name == "memory.dmp" {
        return Match::Candidate {
            category: Category::CrashDump,
            base_confidence: 97,
            reason: "System memory crash dump".into(),
        };
    }

    if lower.contains(r"\crashdumps") || lower.contains("crashpad") {
        return Match::Candidate {
            category: Category::CrashDump,
            base_confidence: 95,
            reason: "Crash dump directory".into(),
        };
    }

    if path.extension().map(|e| e.to_string_lossy().to_lowercase()) == Some("dmp".into()) {
        return Match::Candidate {
            category: Category::CrashDump,
            base_confidence: 93,
            reason: "Crash dump file".into(),
        };
    }

    if lower.contains(r"\mozilla\firefox\profiles") && (lower.contains("cache2") || lower.contains("startupcache")) {
        return Match::Candidate {
            category: Category::BrowserCache,
            base_confidence: 96,
            reason: "Firefox cache".into(),
        };
    }

    if (lower.contains(r"\google\chrome\user data") || lower.contains(r"\microsoft\edge\user data")
        || lower.contains(r"\chromium\user data")) && (lower.contains(r"\cache") || lower.contains("code cache")) {
        return Match::Candidate {
            category: Category::BrowserCache,
            base_confidence: 97,
            reason: "Chromium browser cache".into(),
        };
    }

    if lower.contains(r"\microsoft\edge\user data") && (lower.contains(r"\cache") || lower.contains("code cache")) {
        return Match::Candidate {
            category: Category::BrowserCache,
            base_confidence: 97,
            reason: "Edge browser cache".into(),
        };
    }

    if lower.contains("softwaredistribution\\download") {
        return Match::Candidate {
            category: Category::WindowsUpdateCache,
            base_confidence: 90,
            reason: "Windows Update download cache".into(),
        };
    }

    if lower.contains(r"\nuget\cache")
        || lower.contains(r"\npm-cache")
        || lower.contains(r"\pnpm")
        || lower.contains(r"\yarn\cache")
        || lower.contains(r"\.yarn\berry")
        || lower.contains(r"\pip\cache")
        || lower.contains(r"\.bun\")
        || lower.contains(r"\.cargo\registry\cache")
        || lower.contains(r"\.gradle\caches")
        || lower.contains("packagecache")
    {
        return Match::Candidate {
            category: Category::PackageCache,
            base_confidence: 92,
            reason: "Package manager cache".into(),
        };
    }

    if contains_lowercase(path, &[r"\windowstemp", r"\temp\"]) {
        return Match::Candidate {
            category: Category::Temp,
            base_confidence: 99,
            reason: "Temporary file".into(),
        };
    }

    if path.extension().map(|e| e.to_string_lossy().to_lowercase()) == Some("log".into()) {
        return Match::Candidate {
            category: Category::Logs,
            base_confidence: 85,
            reason: "Log file".into(),
        };
    }

    if contains_lowercase(path, &["driverstore"]) {
        return Match::Candidate {
            category: Category::DriverCache,
            base_confidence: 75,
            reason: "Driver store cache".into(),
        };
    }

    if (name.starts_with("thumbcache") || name.starts_with("iconcache"))
        && lower.contains(r"\microsoft\windows\explorer")
    {
        return Match::Candidate {
            category: Category::ThumbnailCache,
            base_confidence: 96,
            reason: "Windows thumbnail cache database".into(),
        };
    }

    if contains_lowercase(path, &[r"\windows.old", r"\windows~old"]) {
        return Match::Candidate {
            category: Category::OldWindowsInstall,
            base_confidence: 97,
            reason: "File from a previous Windows installation".into(),
        };
    }

    Match::None
}

#[cfg(test)]
mod tests {
    use super::*;

    fn p(path: &str) -> &Path {
        Path::new(path)
    }

    #[test]
    fn protected_extensions_are_protected() {
        assert!(is_protected_extension(p(r"C:\Temp\foo.dll")));
        assert!(is_protected_extension(p(r"C:\Temp\foo.sys")));
        assert!(is_protected_extension(p(r"C:\Temp\setup.exe")));
        assert!(is_protected_extension(p(r"C:\Temp\patch.msi")));
        assert!(!is_protected_extension(p(r"C:\Temp\foo.txt")));
    }

    #[test]
    fn protected_extension_is_classified_as_protected() {
        assert!(matches!(classify(p(r"C:\Windows\Temp\evil.exe")), Match::Protected));
    }

    #[test]
    fn memory_dump_is_crash_dump() {
        match classify(p(r"C:\Users\u\AppData\Local\CrashDumps\memdmp.dmp")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::CrashDump),
            _ => panic!("expected crash dump"),
        }
    }

    #[test]
    fn crashpad_is_crash_dump() {
        match classify(p(r"C:\Users\u\AppData\Local\Google\Chrome\User Data\Crashpad\reports\abc.dmp")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::CrashDump),
            _ => panic!("expected crash dump"),
        }
    }

    #[test]
    fn chrome_cache_is_browser_cache() {
        match classify(p(r"C:\Users\u\AppData\Local\Google\Chrome\User Data\Default\Cache\f_00001")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::BrowserCache),
            _ => panic!("expected browser cache"),
        }
    }

    #[test]
    fn edge_code_cache_is_browser_cache() {
        match classify(p(r"C:\Users\u\AppData\Local\Microsoft\Edge\User Data\Default\Code Cache\js\1.js")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::BrowserCache),
            _ => panic!("expected browser cache"),
        }
    }

    #[test]
    fn windows_update_download_is_update_cache() {
        match classify(p(r"C:\Windows\SoftwareDistribution\Download\1\2.cab")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::WindowsUpdateCache),
            _ => panic!("expected update cache"),
        }
    }

    #[test]
    fn package_caches_are_detected() {
        for path in [
            r"C:\Users\u\AppData\Local\NuGet\Cache\a.nupkg",
            r"C:\Users\u\AppData\Local\npm-cache\_cacache\abc",
            r"C:\Users\u\AppData\Local\pip\cache\http\abc",
            r"C:\ProgramData\packagecache\file",
        ] {
            match classify(p(path)) {
                Match::Candidate { category, .. } => assert_eq!(category, Category::PackageCache, "for {}", path),
                _ => panic!("expected package cache for {}", path),
            }
        }
    }

    #[test]
    fn temp_files_are_detected() {
        match classify(p(r"C:\Users\u\AppData\Local\Temp\foo.tmp")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::Temp),
            _ => panic!("expected temp"),
        }
    }

    #[test]
    fn log_files_are_detected() {
        match classify(p(r"C:\Users\u\AppData\Local\app\installer.log")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::Logs),
            _ => panic!("expected log"),
        }
    }

    #[test]
    fn driver_store_is_driver_cache() {
        match classify(p(r"C:\Windows\DriverStore\FileRepository\foo\file.txt")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::DriverCache),
            _ => panic!("expected driver cache"),
        }
    }

    #[test]
    fn unrelated_file_is_none() {
        assert!(matches!(
            classify(p(r"C:\Users\u\Documents\report.pdf")),
            Match::None
        ));
    }

    #[test]
    fn case_insensitive_classification() {
        match classify(p(r"C:\WINDOWS\SOFTWAREDISTRIBUTION\DOWNLOAD\x\y")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::WindowsUpdateCache),
            _ => panic!("expected case-insensitive match"),
        }
    }

    #[test]
    fn thumbnail_cache_is_detected() {
        for path in [
            r"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\thumbcache_256.db",
            r"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\iconcache_64.db",
        ] {
            match classify(p(path)) {
                Match::Candidate { category, .. } => assert_eq!(category, Category::ThumbnailCache, "for {}", path),
                _ => panic!("expected thumbnail cache for {}", path),
            }
        }
    }

    #[test]
    fn unrelated_explorer_db_is_not_thumbnail_cache() {
        assert!(matches!(
            classify(p(r"C:\Users\u\AppData\Local\Microsoft\Windows\Explorer\otherfile.dat")),
            Match::None
        ));
    }

    #[test]
    fn old_windows_install_is_detected() {
        match classify(p(r"C:\Windows.old\Windows\System32\config\software")) {
            Match::Candidate { category, .. } => assert_eq!(category, Category::OldWindowsInstall),
            _ => panic!("expected old windows install"),
        }
    }

    #[test]
    fn protected_path_model_does_not_flag_windows_old() {
        assert!(!crate::models::is_protected_path(Path::new(r"C:\Windows.old\foo.txt")));
        assert!(crate::models::is_protected_path(Path::new(r"C:\Windows\foo.txt")));
    }

    #[test]
    fn extra_package_managers_are_detected() {
        for path in [
            r"C:\Users\u\AppData\Local\pnpm-store\v3\files\ab",
            r"C:\Users\u\.cargo\registry\cache\index.crates.io-6f17d22bba15001f\abc",
            r"C:\Users\u\.gradle\caches\modules-2\files-2.1\com.example",
            r"C:\Users\u\.yarn\berry\cache\abc",
            r"C:\Users\u\.bun\install\cache\abc",
        ] {
            match classify(p(path)) {
                Match::Candidate { category, .. } => assert_eq!(category, Category::PackageCache, "for {}", path),
                _ => panic!("expected package cache for {}", path),
            }
        }
    }
}