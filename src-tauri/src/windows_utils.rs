use crate::models::DriveInfo;
use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use std::os::windows::fs::MetadataExt;

pub const FILE_ATTRIBUTE_SYSTEM: u32 = 0x4;

fn to_wide(s: &str) -> Vec<u16> {
    OsStr::new(s).encode_wide().collect()
}

fn to_wide_double_null(s: &str) -> Vec<u16> {
    let mut v = OsStr::new(s).encode_wide().collect::<Vec<u16>>();
    v.push(0);
    v.push(0);
    v
}

pub fn drive_type_name(t: u32) -> String {
    match t {
        3 => "fixed".into(),
        2 => "removable".into(),
        4 => "remote".into(),
        5 => "cdrom".into(),
        6 => "ramdisk".into(),
        1 => "no_root".into(),
        _ => "unknown".into(),
    }
}

pub fn list_drives() -> Vec<DriveInfo> {
    let mut result = Vec::new();
    unsafe {
        let mask = windows_sys::Win32::Storage::FileSystem::GetLogicalDrives();
        if mask == 0 {
            return result;
        }
        for i in 0..26 {
            if mask & (1 << i) == 0 {
                continue;
            }
            let letter = (b'A' + i) as char;
            let root = format!("{}:\\", letter);
            let root_wide = to_wide(&root);
            let kind = drive_type_name(windows_sys::Win32::Storage::FileSystem::GetDriveTypeW(
                root_wide.as_ptr(),
            ));
            let mut free_to_caller: u64 = 0;
            let mut total: u64 = 0;
            let mut total_free: u64 = 0;
            let ok = windows_sys::Win32::Storage::FileSystem::GetDiskFreeSpaceExW(
                root_wide.as_ptr(),
                &mut free_to_caller,
                &mut total,
                &mut total_free,
            );
            if ok != 0 && total > 0 {
                result.push(DriveInfo {
                    letter: format!("{}:", letter),
                    kind,
                    total,
                    free: free_to_caller,
                    used: total.saturating_sub(total_free),
                });
            }
        }
    }
    result
}

pub fn is_system_attr(attrs: u32) -> bool {
    attrs & FILE_ATTRIBUTE_SYSTEM != 0
}

pub fn metadata_attrs(path: &std::path::Path) -> Option<u32> {
    std::fs::metadata(path).ok().map(|m| m.file_attributes())
}

pub struct RecycleBinInfo {
    pub size: u64,
    pub count: u64,
}

pub fn query_recycle_bin(root: Option<&str>) -> Option<RecycleBinInfo> {
    unsafe {
        use windows_sys::Win32::UI::Shell::{SHQueryRecycleBinW, SHQUERYRBINFO};
        let mut info: SHQUERYRBINFO = std::mem::zeroed();
        info.cbSize = std::mem::size_of::<SHQUERYRBINFO>() as u32;
        let root_wide = root.map(to_wide);
        let ptr = root_wide
            .as_ref()
            .map(|v| v.as_ptr())
            .unwrap_or(std::ptr::null());
        let hr = SHQueryRecycleBinW(ptr, &mut info);
        if hr != 0 {
            return None;
        }
        Some(RecycleBinInfo {
            size: info.i64Size.max(0) as u64,
            count: info.i64NumItems.max(0) as u64,
        })
    }
}

pub fn empty_recycle_bin() -> bool {
    unsafe {
        use windows_sys::Win32::UI::Shell::{SHEmptyRecycleBinW, SHERB_NOCONFIRMATION, SHERB_NOPROGRESSUI, SHERB_NOSOUND};
        let hr = SHEmptyRecycleBinW(
            std::ptr::null_mut(),
            std::ptr::null(),
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND,
        );
        hr == 0
    }
}

pub fn move_to_recycle_bin(path: &std::path::Path) -> Result<(), String> {
    unsafe {
        use windows_sys::Win32::UI::Shell::{SHFileOperationW, SHFILEOPSTRUCTW, FOF_ALLOWUNDO, FOF_NOCONFIRMATION, FOF_SILENT, FOF_NOERRORUI, FO_DELETE};
        let mut from = to_wide_double_null(&path.to_string_lossy());
        let mut op: SHFILEOPSTRUCTW = std::mem::zeroed();
        op.wFunc = FO_DELETE;
        op.pFrom = from.as_mut_ptr();
        op.pTo = std::ptr::null_mut();
        op.fFlags = (FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI) as u16;
        let ret = SHFileOperationW(&mut op);
        if ret != 0 || op.fAnyOperationsAborted != 0 {
            return Err(format!(
                "Failed to move to recycle bin (code {})",
                ret
            ));
        }
        Ok(())
    }
}

pub fn is_locked(path: &std::path::Path) -> bool {
    match std::fs::File::options().write(true).open(path) {
        Ok(_) => false,
        Err(_) => {
            if let Ok(f) = std::fs::File::open(path) {
                drop(f);
                true
            } else {
                false
            }
        }
    }
}

pub fn has_microsoft_signature(path: &std::path::Path) -> bool {
    if !path.exists() {
        return false;
    }
    let script = format!(
        "(Get-AuthenticodeSignature -LiteralPath '{}').SignerCertificate.Subject",
        path.to_string_lossy().replace('\'', "''")
    );
    let output = std::process::Command::new("powershell")
        .args(["-NoProfile", "-NonInteractive", "-Command", &script])
        .output();
    match output {
        Ok(out) if out.status.success() => {
            let text = String::from_utf8_lossy(&out.stdout).to_lowercase();
            text.contains("microsoft") || text.contains("windows")
        }
        _ => false,
    }
}
