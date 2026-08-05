use std::path::PathBuf;

pub fn data_root() -> PathBuf {
    let pd = PathBuf::from(r"C:\ProgramData\SafeDisk");
    if std::fs::create_dir_all(&pd).is_ok() {
        return pd;
    }
    let local = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    let alt = local.join("SafeDisk");
    let _ = std::fs::create_dir_all(&alt);
    alt
}

pub fn audit_dir() -> PathBuf {
    let d = data_root().join("audit");
    let _ = std::fs::create_dir_all(&d);
    d
}

pub fn quarantine_dir() -> PathBuf {
    let d = data_root().join("quarantine");
    let _ = std::fs::create_dir_all(&d);
    d
}

pub fn reports_dir() -> PathBuf {
    let d = data_root().join("reports");
    let _ = std::fs::create_dir_all(&d);
    d
}
