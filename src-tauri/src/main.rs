#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(all(windows, feature = "embed-webview2"))]
mod embedded_webview2 {
    use std::io::Cursor;
    use std::path::{Path, PathBuf};

    const RUNTIME_ZIP: &[u8] = include_bytes!("../webview2-runtime.zip");

    pub fn setup() {
        if let Some(dir) = ensure_runtime() {
            std::env::set_var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", &dir);
        }
    }

    fn ensure_runtime() -> Option<PathBuf> {
        let root = cache_root();
        if let Some(dir) = find_runtime(&root) {
            return Some(dir);
        }
        let _ = std::fs::create_dir_all(&root);
        extract(RUNTIME_ZIP, &root).ok()?;
        find_runtime(&root)
    }

    fn cache_root() -> PathBuf {
        let base = std::env::var_os("LOCALAPPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(std::env::temp_dir);
        base.join("SafeDisk").join("WebView2Runtime")
    }

    fn find_runtime(root: &Path) -> Option<PathBuf> {
        let entries = std::fs::read_dir(root).ok()?;
        for entry in entries.flatten() {
            let p = entry.path();
            if p.is_dir() && p.join("msedgewebview2.exe").exists() {
                return Some(p);
            }
        }
        None
    }

    fn extract(zip_bytes: &[u8], dest: &Path) -> Result<(), Box<dyn std::error::Error>> {
        let mut archive = zip::ZipArchive::new(Cursor::new(zip_bytes))?;
        for i in 0..archive.len() {
            let mut entry = archive.by_index(i)?;
            let name = entry.name().replace('\\', "/");
            let out_path = dest.join(name);
            if entry.is_dir() {
                std::fs::create_dir_all(&out_path)?;
                continue;
            }
            if let Some(parent) = out_path.parent() {
                std::fs::create_dir_all(parent)?;
            }
            let mut f = std::fs::File::create(&out_path)?;
            std::io::copy(&mut entry, &mut f)?;
        }
        Ok(())
    }
}

#[cfg(all(windows, not(feature = "embed-webview2")))]
mod webview2_check {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;

    const DOWNLOAD_URL: &str = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    const REG_SUBKEYS: [&str; 2] = [
        r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
        r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
    ];

    fn wide(s: &str) -> Vec<u16> {
        OsStr::new(s).encode_wide().chain(Some(0)).collect()
    }

    pub fn runtime_installed() -> bool {
        use windows_sys::Win32::System::Registry::*;
        unsafe {
            for key in REG_SUBKEYS {
                let key_w = wide(key);
                let mut hkey: HKEY = std::ptr::null_mut();
                if RegOpenKeyExW(
                    HKEY_LOCAL_MACHINE,
                    key_w.as_ptr(),
                    0,
                    KEY_READ | KEY_WOW64_64KEY,
                    &mut hkey,
                ) != 0
                {
                    continue;
                }
                let name_w = wide("pv");
                let mut data = [0u8; 128];
                let mut size = data.len() as u32;
                let mut kind = 0u32;
                let found = RegQueryValueExW(
                    hkey,
                    name_w.as_ptr(),
                    std::ptr::null(),
                    &mut kind,
                    data.as_mut_ptr(),
                    &mut size,
                ) == 0
                    && size > 0;
                RegCloseKey(hkey);
                if found {
                    return true;
                }
            }
        }
        false
    }

    pub fn show_missing_dialog() {
        use windows_sys::Win32::UI::Shell::ShellExecuteW;
        use windows_sys::Win32::UI::WindowsAndMessaging::{
            IDYES, MB_DEFBUTTON1, MB_ICONINFORMATION, MB_SETFOREGROUND, MB_YESNO, MessageBoxW,
            SW_SHOWNORMAL,
        };

        let title = wide("SafeDisk Cleaner");
        let msg = wide(
            "Для роботи програми потрібен Microsoft Edge WebView2 Runtime.\n\n\
             Він не встановлений на цьому комп'ютері.\n\
             Натисніть «Так», щоб завантажити та встановити його (безкоштовно).",
        );

        unsafe {
            let res = MessageBoxW(
                std::ptr::null_mut(),
                msg.as_ptr(),
                title.as_ptr(),
                MB_YESNO | MB_ICONINFORMATION | MB_DEFBUTTON1 | MB_SETFOREGROUND,
            );
            if res == IDYES {
                let op = wide("open");
                let url = wide(DOWNLOAD_URL);
                ShellExecuteW(
                    std::ptr::null_mut(),
                    op.as_ptr(),
                    url.as_ptr(),
                    std::ptr::null(),
                    std::ptr::null(),
                    SW_SHOWNORMAL,
                );
            }
        }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if !args.is_empty() {
        std::process::exit(safedisk_cleaner_lib::cli_public::run_cli(&args));
    }

    #[cfg(all(windows, feature = "embed-webview2"))]
    embedded_webview2::setup();

    #[cfg(all(windows, not(feature = "embed-webview2")))]
    if !webview2_check::runtime_installed() {
        webview2_check::show_missing_dialog();
        std::process::exit(1);
    }

    safedisk_cleaner_lib::run();
}
