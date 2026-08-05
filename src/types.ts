export type Category =
  | "temp"
  | "crash_dump"
  | "browser_cache"
  | "recycle_bin"
  | "logs"
  | "windows_update_cache"
  | "driver_cache"
  | "package_cache"
  | "duplicate_files"
  | "large_unused_files"
  | "thumbnail_cache"
  | "old_windows_install";

export type RiskLevel = "safe" | "medium" | "advanced";
export type CandidateAction = "delete" | "review" | "keep";
export type CleanupStatus =
  | "deleted"
  | "quarantined"
  | "recycled"
  | "skipped"
  | "failed"
  | "would_delete";

export interface Candidate {
  path: string;
  size: number;
  category: Category;
  confidence: number;
  action: CandidateAction;
  reason: string;
  last_modified: string | null;
  last_access_days: number | null;
  risk_level: RiskLevel;
}

export interface CategoryStats {
  category: Category;
  risk_level: RiskLevel;
  count: number;
  size: number;
  potential: number;
}

export interface ScanSummary {
  scanned_dirs: number;
  scanned_files: number;
  elapsed_ms: number;
  total_potential: number;
  total_candidates: number;
  categories: CategoryStats[];
}

export interface ScanResult {
  candidates: Candidate[];
  summary: ScanSummary;
}

export interface ScanProgress {
  current_root: string;
  files_scanned: number;
  dirs_scanned: number;
  candidates_found: number;
  percent: number;
  finished: boolean;
}

export interface CleanupProgress {
  processed: number;
  total: number;
  current_path: string;
  status: string;
  percent: number;
  finished: boolean;
}

export interface CleanupEntry {
  path: string;
  size: number;
  category: Category;
  confidence: number;
  status: CleanupStatus;
  detail: string;
}

export interface CleanupResult {
  mode: string;
  processed: number;
  deleted: number;
  freed_bytes: number;
  entries: CleanupEntry[];
}

export interface AuditEntry {
  date: string;
  action: string;
  path: string;
  size: number;
  success: boolean;
  detail: string;
}

export interface QuarantineEntry {
  id: string;
  original_path: string;
  quarantined_path: string;
  size: number;
  quarantined_at: string;
  expires_at: string;
}

export interface DriveInfo {
  letter: string;
  kind: string;
  total: number;
  free: number;
  used: number;
}

export interface UpdateInfo {
  available: boolean;
  latest_version: string;
  current_version: string;
  download_url: string;
}

export const CATEGORY_LABELS: Record<Category, string> = {
  temp: "Temp files",
  crash_dump: "Crash dumps",
  browser_cache: "Browser cache",
  recycle_bin: "Recycle Bin",
  logs: "Logs",
  windows_update_cache: "Windows Update cache",
  driver_cache: "Driver cache",
  package_cache: "Package cache",
  duplicate_files: "Duplicate files",
  large_unused_files: "Large unused files",
  thumbnail_cache: "Thumbnail cache",
  old_windows_install: "Old Windows installation",
};

export const RISK_LABELS: Record<RiskLevel, string> = {
  safe: "Safe",
  medium: "Medium",
  advanced: "Advanced",
};

export function humanSize(bytes: number): string {
  const units = ["B", "KB", "MB", "GB", "TB"];
  let v = bytes;
  let u = 0;
  while (v >= 1024 && u < units.length - 1) {
    v /= 1024;
    u += 1;
  }
  return u === 0 ? `${v} ${units[u]}` : `${v.toFixed(1)} ${units[u]}`;
}
