use crate::models::*;

fn human_size(bytes: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut v = bytes as f64;
    let mut u = 0;
    while v >= 1024.0 && u < UNITS.len() - 1 {
        v /= 1024.0;
        u += 1;
    }
    if u == 0 {
        format!("{} {}", bytes, UNITS[u])
    } else {
        format!("{:.1} {}", v, UNITS[u])
    }
}

fn print_candidate(c: &Candidate) {
    println!(
        "  [{}%] {:>10}  {:<20} {}",
        c.confidence,
        human_size(c.size),
        c.category.label(),
        c.path
    );
    println!("        reason: {} | {} | action: {:?}", c.reason, crate::confidence::recommendation(c.confidence), c.action);
}

fn parse_roots(args: &[String]) -> Vec<String> {
    let mut roots = Vec::new();
    let mut i = 0;
    while i < args.len() {
        if args[i] == "--roots" {
            if let Some(list) = args.get(i + 1) {
                roots.extend(list.split(',').map(|s| s.trim().to_string()).filter(|s| !s.is_empty()));
                i += 1;
            }
        }
        i += 1;
    }
    roots
}

pub fn run(args: &[String]) -> i32 {
    if args.is_empty() {
        print_help();
        return 0;
    }

    match args[0].as_str() {
        "analyze" => cmd_analyze(args),
        "clean" => cmd_clean(args),
        "drives" => cmd_drives(),
        "audit" => cmd_audit(),
        "quarantine" => cmd_quarantine(args),
        "update" => cmd_update(),
        "duplicates" => cmd_duplicates(args),
        "help" | "--help" | "-h" => {
            print_help();
            0
        }
        other => {
            eprintln!("Unknown command: {}", other);
            print_help();
            1
        }
    }
}

fn print_help() {
    println!(
        r#"SafeDisk Cleaner {}

USAGE:
  safedisk analyze [--medium] [--advanced] [--roots <dir1,dir2>] [--min-confidence <n>] [--recency-days <n>] [--report]
  safedisk clean [--auto] [--dry-run] [--interactive] [--roots <dir1,dir2>] [--report]
  safedisk duplicates --roots <dir1,dir2>
  safedisk drives
  safedisk audit
  safedisk quarantine list
  safedisk quarantine restore <id>
  safedisk quarantine remove <id>
  safedisk quarantine purge
  safedisk update
"#,
        env!("CARGO_PKG_VERSION")
    );
}

fn flags(args: &[String]) -> (bool, bool) {
    (
        args.contains(&"--medium".to_string()),
        args.contains(&"--advanced".to_string()),
    )
}

fn cmd_analyze(args: &[String]) -> i32 {
    let (medium, advanced) = flags(args);
    let roots = parse_roots(args);
    let mut opts = ScanOptions {
        roots,
        include_medium: medium,
        include_advanced: advanced,
        ..Default::default()
    };
    for (i, a) in args.iter().enumerate() {
        if a == "--min-confidence" {
            if let Some(v) = args.get(i + 1).and_then(|s| s.parse().ok()) {
                opts.min_confidence = v;
            }
        }
        if a == "--recency-days" {
            if let Some(v) = args.get(i + 1).and_then(|s| s.parse().ok()) {
                opts.recency_days = v;
            }
        }
    }

    println!("Scanning...");
    let result = crate::scanner::scan(&opts);
    let s = &result.summary;
    println!(
        "Scanned {} files in {} dirs ({} ms)",
        s.scanned_files, s.scanned_dirs, s.elapsed_ms
    );
    println!("Potential free space: {}", human_size(s.total_potential));
    println!();
    println!("Categories:");
    for c in &s.categories {
        println!(
            "  {:>10}  {:>6} files  {:>10} ({})",
            human_size(c.potential),
            c.count,
            human_size(c.size),
            c.category.label()
        );
    }
    println!();
    println!("Top candidates:");
    for c in result.candidates.iter().take(20) {
        print_candidate(c);
    }
    if args.contains(&"--report".to_string()) {
        match crate::cleanup::write_scan_report(&result) {
            Ok(p) => println!("\nReport written to {}", p.display()),
            Err(e) => eprintln!("Failed to write report: {}", e),
        }
    }
    0
}

fn cmd_clean(args: &[String]) -> i32 {
    let (medium, advanced) = flags(args);
    let roots = parse_roots(args);
    let mut opts = ScanOptions {
        roots,
        include_medium: medium,
        include_advanced: advanced,
        ..Default::default()
    };
    for (i, a) in args.iter().enumerate() {
        if a == "--min-confidence" {
            if let Some(v) = args.get(i + 1).and_then(|s| s.parse().ok()) {
                opts.min_confidence = v;
            }
        }
    }

    let mode = if args.contains(&"--dry-run".to_string()) {
        CleanMode::DryRun
    } else if args.contains(&"--auto".to_string()) {
        CleanMode::Auto
    } else if args.contains(&"--interactive".to_string()) {
        CleanMode::Interactive
    } else {
        CleanMode::Interactive
    };

    println!("Scanning...");
    let result = crate::scanner::scan(&opts);
    let s = &result.summary;
    println!(
        "Scanned {} files in {} dirs. Potential: {}",
        s.scanned_files,
        s.scanned_dirs,
        human_size(s.total_potential)
    );

    let mut confirmed: Vec<Candidate> = Vec::new();

    if matches!(mode, CleanMode::Interactive) {
        for c in &result.candidates {
            if c.action == CandidateAction::Keep {
                continue;
            }
            print_candidate(c);
            print!("  Delete? [y/n/a=all/s=skip-all/done]: ");
            use std::io::Write;
            std::io::stdout().flush().ok();
            let mut line = String::new();
            if std::io::stdin().read_line(&mut line).is_err() {
                break;
            }
            match line.trim().to_lowercase().as_str() {
                "y" => confirmed.push(c.clone()),
                "a" => {
                    confirmed.push(c.clone());
                    confirmed.extend(
                        result
                            .candidates
                            .iter()
                            .filter(|x| x.action != CandidateAction::Keep)
                            .cloned(),
                    );
                    break;
                }
                "s" | "done" => break,
                _ => {}
            }
        }
    } else {
        confirmed = result
            .candidates
            .into_iter()
            .filter(|c| c.action == CandidateAction::Delete)
            .collect();
    }

    let cleanup_opts = CleanupOptions {
        mode,
        ..Default::default()
    };
    let cleanup = crate::cleanup::run(&confirmed, &cleanup_opts);
    println!();
    println!(
        "Processed {} items. Freed: {}. Deleted: {}.",
        cleanup.processed,
        human_size(cleanup.freed_bytes),
        cleanup.deleted
    );
    for e in &cleanup.entries {
        println!("  {:?} {} {}", e.status, human_size(e.size), e.path);
    }
    if args.contains(&"--report".to_string()) {
        match crate::cleanup::write_report(&cleanup) {
            Ok(p) => println!("\nReport written to {}", p.display()),
            Err(e) => eprintln!("Failed to write report: {}", e),
        }
    }
    0
}

fn cmd_duplicates(args: &[String]) -> i32 {
    let roots = parse_roots(args);
    if roots.is_empty() {
        eprintln!("Usage: safedisk duplicates --roots <dir1,dir2>");
        return 1;
    }
    println!("Hashing files (this may take a while)...");
    let result = crate::scanner::scan_duplicates(roots);
    println!("Found {} duplicate candidates ({} total).", result.candidates.len(), human_size(result.summary.total_potential));
    for c in result.candidates.iter().take(30) {
        println!("  {}  {}", human_size(c.size), c.path);
        println!("        {}", c.reason);
    }
    0
}

fn cmd_drives() -> i32 {
    println!("{:<5} {:<12} {:>12} {:>12} {:>12}", "Drive", "Type", "Total", "Used", "Free");
    for d in crate::windows_utils::list_drives() {
        println!(
            "{:<5} {:<12} {:>12} {:>12} {:>12}",
            d.letter,
            d.kind,
            human_size(d.total),
            human_size(d.used),
            human_size(d.free)
        );
    }
    0
}

fn cmd_audit() -> i32 {
    let entries = crate::audit::read_all();
    println!("Audit log ({} entries):", entries.len());
    for e in entries.iter().rev().take(50) {
        println!(
            "  {}  {:<8}  {:>10}  {}  {}",
            e.date,
            e.action,
            human_size(e.size),
            if e.success { "OK" } else { "FAIL" },
            e.path
        );
    }
    0
}

fn cmd_quarantine(args: &[String]) -> i32 {
    let sub = args.get(1).map(|s| s.as_str()).unwrap_or("list");
    match sub {
        "list" => {
            let items = crate::quarantine::list_quarantine();
            println!("Quarantine ({} items):", items.len());
            for e in items {
                println!(
                    "  {}  {:>10}  {}  ->  {}",
                    e.id,
                    human_size(e.size),
                    e.quarantined_at,
                    e.original_path
                );
            }
            0
        }
        "restore" => match args.get(2) {
            Some(id) => match crate::quarantine::restore_quarantine(id) {
                Ok(()) => {
                    println!("Restored {}", id);
                    0
                }
                Err(e) => {
                    eprintln!("Failed: {}", e);
                    1
                }
            },
            None => {
                eprintln!("Usage: safedisk quarantine restore <id>");
                1
            }
        },
        "remove" => match args.get(2) {
            Some(id) => match crate::quarantine::remove_quarantine(id) {
                Ok(()) => {
                    println!("Removed {}", id);
                    0
                }
                Err(e) => {
                    eprintln!("Failed: {}", e);
                    1
                }
            },
            None => {
                eprintln!("Usage: safedisk quarantine remove <id>");
                1
            }
        },
        "purge" => {
            let n = crate::quarantine::purge_expired(14);
            println!("Purged {} expired items", n);
            0
        }
        other => {
            eprintln!("Unknown subcommand: {}", other);
            1
        }
    }
}

fn cmd_update() -> i32 {
    let info = crate::update::check_for_update();
    println!("Current version: {}", info.current_version);
    println!("Latest version:  {}", info.latest_version);
    println!(
        "Update {}",
        if info.available {
            format!("available: {}", info.download_url)
        } else {
            "not available".into()
        }
    );
    0
}
