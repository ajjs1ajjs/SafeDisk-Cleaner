use crate::models::UpdateInfo;

const REPO: &str = "ajjs1ajjs/SafeDisk-Cleaner";

fn semver_parse(v: &str) -> Option<(u32, u32, u32)> {
    let parts: Vec<&str> = v.splitn(3, '.').collect();
    if parts.len() < 3 {
        return None;
    }
    Some((
        parts[0].parse().ok()?,
        parts[1].parse().ok()?,
        parts[2].parse().ok()?,
    ))
}

pub fn check_for_update() -> UpdateInfo {
    let current = env!("CARGO_PKG_VERSION").to_string();
    let url = format!("https://api.github.com/repos/{}/releases/latest", REPO);

    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(5))
        .user_agent("SafeDisk-Cleaner")
        .build();
    let client = match client {
        Ok(c) => c,
        Err(_) => {
            return UpdateInfo {
                available: false,
                latest_version: String::new(),
                current_version: current,
                download_url: String::new(),
            }
        }
    };

    let resp: Result<serde_json::Value, _> = client
        .get(&url)
        .header("Accept", "application/vnd.github+json")
        .send()
        .and_then(|r| r.json());
    let resp = match resp {
        Ok(v) => v,
        Err(_) => {
            return UpdateInfo {
                available: false,
                latest_version: String::new(),
                current_version: current,
                download_url: String::new(),
            }
        }
    };

    let tag = resp["tag_name"].as_str().unwrap_or("");
    let html_url = resp["html_url"].as_str().unwrap_or("");
    if tag.is_empty() || html_url.is_empty() {
        return UpdateInfo {
            available: false,
            latest_version: String::new(),
            current_version: current,
            download_url: String::new(),
        };
    }

    let latest_ver = tag.trim_start_matches('v');
    let latest = semver_parse(latest_ver).unwrap_or((0, 0, 0));
    let cur = semver_parse(&current).unwrap_or((0, 0, 0));

    UpdateInfo {
        available: latest > cur,
        latest_version: tag.to_string(),
        current_version: current,
        download_url: format!("https://github.com/{}/releases/tag/{}", REPO, tag),
    }
}
