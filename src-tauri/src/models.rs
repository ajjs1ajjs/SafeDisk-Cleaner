use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum Category {
    Temp,
    CrashDump,
    BrowserCache,
    RecycleBin,
    Logs,
    WindowsUpdateCache,
    DriverCache,
    PackageCache,
    DuplicateFiles,
    LargeUnusedFiles,
    ThumbnailCache,
    OldWindowsInstall,
}

impl Category {
    pub fn label(&self) -> &'static str {
        match self {
            Category::Temp => "Temp files",
            Category::CrashDump => "Crash dumps",
            Category::BrowserCache => "Browser cache",
            Category::RecycleBin => "Recycle Bin",
            Category::Logs => "Logs",
            Category::WindowsUpdateCache => "Windows Update cache",
            Category::DriverCache => "Driver cache",
            Category::PackageCache => "Package cache",
            Category::DuplicateFiles => "Duplicate files",
            Category::LargeUnusedFiles => "Large unused files",
            Category::ThumbnailCache => "Thumbnail cache",
            Category::OldWindowsInstall => "Old Windows installation",
        }
    }

    pub fn risk_level(&self) -> RiskLevel {
        match self {
            Category::Temp
            | Category::CrashDump
            | Category::BrowserCache
            | Category::RecycleBin
            | Category::Logs
            | Category::ThumbnailCache => RiskLevel::Safe,
            Category::WindowsUpdateCache
            | Category::DriverCache
            | Category::PackageCache => RiskLevel::Medium,
            Category::DuplicateFiles
            | Category::LargeUnusedFiles
            | Category::OldWindowsInstall => RiskLevel::Advanced,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum RiskLevel {
    Safe,
    Medium,
    Advanced,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum CleanMode {
    Analyze,
    DryRun,
    Interactive,
    Auto,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum CandidateAction {
    Delete,
    Review,
    Keep,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Candidate {
    pub path: String,
    pub size: u64,
    pub category: Category,
    pub confidence: u8,
    pub action: CandidateAction,
    pub reason: String,
    pub last_modified: Option<String>,
    pub last_access_days: Option<u64>,
    pub risk_level: RiskLevel,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CategoryStats {
    pub category: Category,
    pub risk_level: RiskLevel,
    pub count: usize,
    pub size: u64,
    pub potential: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScanSummary {
    pub scanned_dirs: u64,
    pub scanned_files: u64,
    pub elapsed_ms: u128,
    pub total_potential: u64,
    pub total_candidates: usize,
    pub categories: Vec<CategoryStats>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScanResult {
    pub candidates: Vec<Candidate>,
    pub summary: ScanSummary,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScanProgress {
    pub current_root: String,
    pub files_scanned: u64,
    pub dirs_scanned: u64,
    pub candidates_found: u64,
    pub percent: f64,
    pub finished: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CleanupProgress {
    pub processed: u64,
    pub total: u64,
    pub current_path: String,
    pub status: String,
    pub percent: f64,
    pub finished: bool,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
pub struct CleanupOptions {
    pub mode: CleanMode,
    pub quarantine_retention_days: u64,
    pub move_to_recycle_bin: bool,
    pub auto_threshold: u8,
}

impl Default for CleanupOptions {
    fn default() -> Self {
        Self {
            mode: CleanMode::Interactive,
            quarantine_retention_days: 14,
            move_to_recycle_bin: true,
            auto_threshold: 95,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ScanOptions {
    pub roots: Vec<String>,
    pub include_medium: bool,
    pub include_advanced: bool,
    pub min_confidence: u8,
    pub recency_days: u64,
}

impl Default for ScanOptions {
    fn default() -> Self {
        Self {
            roots: Vec::new(),
            include_medium: false,
            include_advanced: false,
            min_confidence: 50,
            recency_days: 7,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CleanupEntry {
    pub path: String,
    pub size: u64,
    pub category: Category,
    pub confidence: u8,
    pub status: CleanupStatus,
    pub detail: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum CleanupStatus {
    Deleted,
    Quarantined,
    Recycled,
    Skipped,
    Failed,
    WouldDelete,
}

impl CleanupStatus {
    pub fn as_str(&self) -> &'static str {
        match self {
            CleanupStatus::Deleted => "deleted",
            CleanupStatus::Quarantined => "quarantined",
            CleanupStatus::Recycled => "recycled",
            CleanupStatus::Skipped => "skipped",
            CleanupStatus::Failed => "failed",
            CleanupStatus::WouldDelete => "would_delete",
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CleanupResult {
    pub mode: CleanMode,
    pub processed: usize,
    pub deleted: usize,
    pub freed_bytes: u64,
    pub entries: Vec<CleanupEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AuditEntry {
    pub date: String,
    pub action: String,
    pub path: String,
    pub size: u64,
    pub success: bool,
    pub detail: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct QuarantineEntry {
    pub id: String,
    pub original_path: String,
    pub quarantined_path: String,
    pub size: u64,
    pub quarantined_at: String,
    pub expires_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DriveInfo {
    pub letter: String,
    pub kind: String,
    pub total: u64,
    pub free: u64,
    pub used: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UpdateInfo {
    pub available: bool,
    pub latest_version: String,
    pub current_version: String,
    pub download_url: String,
}

pub fn is_protected_path(path: &Path) -> bool {
    let lower = path.to_string_lossy().to_lowercase();
    let needles = [
        r"\windows\",
        r"c:\windows\",
        r"c:\program files",
        r"c:\program files (x86)",
        r"c:\programdata",
        r"c:\programdata\",
        r"\system32",
        r"\syswow64",
        r"\drivers\",
        r"\efi\",
        r"\recovery\",
        r"\boot\",
        r"$recycle.bin",
        r"\system volume information",
    ];
    needles.iter().any(|n| lower.contains(n))
}
