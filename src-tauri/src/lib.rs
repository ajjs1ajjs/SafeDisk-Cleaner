mod audit;
mod cli;
mod cleanup;
mod confidence;
mod models;
mod paths;
mod quarantine;
mod rules;
mod safety;
mod scanner;
mod update;
mod windows_utils;

pub mod cli_public {
    pub use crate::cli::run as run_cli;
}

use models::*;

#[tauri::command]
fn list_drives_command() -> Vec<DriveInfo> {
    windows_utils::list_drives()
}

#[tauri::command]
fn get_data_root() -> String {
    paths::data_root().to_string_lossy().to_string()
}

#[tauri::command]
async fn scan_command(
    app: tauri::AppHandle,
    roots: Vec<String>,
    include_medium: bool,
    include_advanced: bool,
    min_confidence: u8,
    recency_days: u64,
) -> Result<ScanResult, String> {
    use tauri::Emitter;
    tauri::async_runtime::spawn_blocking(move || {
        let opts = ScanOptions {
            roots,
            include_medium,
            include_advanced,
            min_confidence: if min_confidence == 0 { 50 } else { min_confidence },
            recency_days: if recency_days == 0 { 3 } else { recency_days },
        };
        let result = scanner::scan_with_progress(&opts, |p| {
            let _ = app.emit("scan-progress", &p);
        });
        Ok(result)
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn scan_duplicates_command(roots: Vec<String>) -> Result<ScanResult, String> {
    tauri::async_runtime::spawn_blocking(move || Ok(scanner::scan_duplicates(roots)))
        .await
        .map_err(|e| e.to_string())?
}

#[tauri::command]
async fn cleanup_command(
    app: tauri::AppHandle,
    candidates: Vec<Candidate>,
    mode: String,
    quarantine_retention_days: u64,
    move_to_recycle_bin: bool,
    auto_threshold: u8,
) -> Result<CleanupResult, String> {
    use tauri::Emitter;
    tauri::async_runtime::spawn_blocking(move || {
        let mode = match mode.as_str() {
            "dry-run" => CleanMode::DryRun,
            "auto" => CleanMode::Auto,
            "interactive" => CleanMode::Interactive,
            _ => CleanMode::Interactive,
        };
        let opts = CleanupOptions {
            mode,
            quarantine_retention_days: if quarantine_retention_days == 0 { 14 } else { quarantine_retention_days },
            move_to_recycle_bin,
            auto_threshold: if auto_threshold == 0 { 95 } else { auto_threshold },
        };
        let on_progress = |p: &CleanupProgress| {
            let _ = app.emit("cleanup-progress", p);
        };
        Ok(cleanup::run_with_progress(&candidates, &opts, Some(&on_progress)))
    })
    .await
    .map_err(|e| e.to_string())?
}

#[tauri::command]
fn get_audit_log() -> Vec<AuditEntry> {
    audit::read_all()
}

#[tauri::command]
fn clear_audit_log() {
    audit::clear();
}

#[tauri::command]
fn get_quarantine() -> Vec<QuarantineEntry> {
    quarantine::list_quarantine()
}

#[tauri::command]
fn restore_quarantine_command(id: String) -> Result<(), String> {
    quarantine::restore_quarantine(&id)
}

#[tauri::command]
fn remove_quarantine_command(id: String) -> Result<(), String> {
    quarantine::remove_quarantine(&id)
}

#[tauri::command]
fn empty_quarantine_command() -> Result<usize, String> {
    quarantine::empty_quarantine()
}

#[tauri::command]
fn empty_recycle_bin_command() -> Result<(), String> {
    if windows_utils::empty_recycle_bin() {
        Ok(())
    } else {
        Err("Failed to empty Recycle Bin".into())
    }
}

#[tauri::command]
fn check_update() -> UpdateInfo {
    update::check_for_update()
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .setup(|_app| {
            let _ = paths::data_root();
            let _ = paths::audit_dir();
            let _ = paths::quarantine_dir();
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            list_drives_command,
            get_data_root,
            scan_command,
            scan_duplicates_command,
            cleanup_command,
            get_audit_log,
            clear_audit_log,
            get_quarantine,
            restore_quarantine_command,
            remove_quarantine_command,
            empty_quarantine_command,
            empty_recycle_bin_command,
            check_update,
        ])
        .run(tauri::generate_context!())
        .expect("error while running SafeDisk Cleaner");
}
