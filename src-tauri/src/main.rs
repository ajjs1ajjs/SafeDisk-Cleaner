#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if !args.is_empty() {
        std::process::exit(safedisk_cleaner_lib::cli_public::run_cli(&args));
    }
    safedisk_cleaner_lib::run();
}
