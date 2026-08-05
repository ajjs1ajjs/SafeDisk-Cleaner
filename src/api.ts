import { invoke } from "@tauri-apps/api/core";
import type {
  Candidate,
  CleanupResult,
  DriveInfo,
  AuditEntry,
  QuarantineEntry,
  ScanResult,
  UpdateInfo,
} from "./types";

export function listDrives(): Promise<DriveInfo[]> {
  return invoke<DriveInfo[]>("list_drives_command");
}

export function getDataRoot(): Promise<string> {
  return invoke<string>("get_data_root");
}

export function scan(
  roots: string[],
  includeMedium: boolean,
  includeAdvanced: boolean,
  minConfidence: number,
  recencyDays: number
): Promise<ScanResult> {
  return invoke<ScanResult>("scan_command", {
    roots,
    includeMedium,
    includeAdvanced,
    minConfidence,
    recencyDays,
  });
}

export function scanDuplicates(roots: string[]): Promise<ScanResult> {
  return invoke<ScanResult>("scan_duplicates_command", { roots });
}

export function cleanup(
  candidates: Candidate[],
  mode: "dry-run" | "auto" | "interactive",
  quarantineRetentionDays: number,
  moveToRecycleBin: boolean,
  autoThreshold: number
): Promise<CleanupResult> {
  return invoke<CleanupResult>("cleanup_command", {
    candidates,
    mode,
    quarantineRetentionDays,
    moveToRecycleBin,
    autoThreshold,
  });
}

export function getAuditLog(): Promise<AuditEntry[]> {
  return invoke<AuditEntry[]>("get_audit_log");
}

export function clearAuditLog(): Promise<void> {
  return invoke<void>("clear_audit_log");
}

export function getQuarantine(): Promise<QuarantineEntry[]> {
  return invoke<QuarantineEntry[]>("get_quarantine");
}

export function restoreQuarantine(id: string): Promise<void> {
  return invoke<void>("restore_quarantine_command", { id });
}

export function removeQuarantine(id: string): Promise<void> {
  return invoke<void>("remove_quarantine_command", { id });
}

export function emptyQuarantine(): Promise<number> {
  return invoke<number>("empty_quarantine_command");
}

export function emptyRecycleBin(): Promise<void> {
  return invoke<void>("empty_recycle_bin_command");
}

export function checkUpdate(): Promise<UpdateInfo> {
  return invoke<UpdateInfo>("check_update");
}
