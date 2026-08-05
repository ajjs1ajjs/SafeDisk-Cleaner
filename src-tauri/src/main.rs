#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(windows)]
fn setup_bundled_webview2_runtime() {
    if std::env::var_os("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER").is_some() {
        return;
    }
    let Ok(exe) = std::env::current_exe() else {
        return;
    };
    let Some(dir) = exe.parent() else {
        return;
    };
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            let name = path.file_name().map(|n| n.to_string_lossy().into_owned());
            if let Some(name) = name {
                if name.starts_with("Microsoft.WebView2.FixedVersionRuntime") {
                    std::env::set_var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", &path);
                    break;
                }
            }
        }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if !args.is_empty() {
        std::process::exit(safedisk_cleaner_lib::cli_public::run_cli(&args));
    }
    setup_bundled_webview2_runtime();
    safedisk_cleaner_lib::run();
}
