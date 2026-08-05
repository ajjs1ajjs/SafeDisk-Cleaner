use crate::models::{CandidateAction, Category, RiskLevel};
use std::time::{Duration, SystemTime};

pub struct ConfidenceInput {
    pub base: u8,
    pub category: Category,
    pub size: u64,
    pub last_access: Option<SystemTime>,
    pub recency_days: u64,
    pub locked: bool,
    pub system_attr: bool,
}

pub fn compute(input: ConfidenceInput) -> u8 {
    let mut score = input.base as i32;

    if let Some(la) = input.last_access {
        let days = elapsed_days(la);
        if let Some(days) = days {
            if days < input.recency_days {
                score -= 40;
            } else {
                score += (days.min(365) as i32) / 15;
            }
        }
    }

    if input.locked {
        score -= 60;
    }

    if input.system_attr {
        score -= 70;
    }

    match input.category.risk_level() {
        RiskLevel::Safe => {}
        RiskLevel::Medium => score -= 8,
        RiskLevel::Advanced => score -= 15,
    }

    if input.size >= 1 << 30 {
        score -= 5;
    } else if input.size >= 512 << 20 {
        score -= 2;
    }

    score.clamp(0, 100) as u8
}

pub fn action_for(confidence: u8, risk: RiskLevel) -> CandidateAction {
    match risk {
        RiskLevel::Safe => {
            if confidence >= 95 {
                CandidateAction::Delete
            } else if confidence >= 80 {
                CandidateAction::Review
            } else {
                CandidateAction::Keep
            }
        }
        RiskLevel::Medium => {
            if confidence >= 80 {
                CandidateAction::Review
            } else {
                CandidateAction::Keep
            }
        }
        RiskLevel::Advanced => CandidateAction::Keep,
    }
}

pub fn recommendation(confidence: u8) -> &'static str {
    match confidence {
        95..=100 => "Delete",
        80..=94 => "Probably safe",
        50..=79 => "Needs review",
        _ => "Do not touch",
    }
}

pub fn elapsed_days(t: SystemTime) -> Option<u64> {
    let now = SystemTime::now();
    let d = now.duration_since(t).unwrap_or(Duration::ZERO);
    Some(d.as_secs() / 86400)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::{CandidateAction, Category};
    use std::time::{Duration, UNIX_EPOCH};

    fn input(base: u8, category: Category) -> ConfidenceInput {
        ConfidenceInput {
            base,
            category,
            size: 1024,
            last_access: None,
            recency_days: 7,
            locked: false,
            system_attr: false,
        }
    }

    #[test]
    fn base_confidence_is_preserved_without_factors() {
        let c = compute(input(95, Category::Temp));
        assert_eq!(c, 95);
    }

    #[test]
    fn recent_access_reduces_score() {
        let mut i = input(99, Category::Temp);
        i.last_access = Some(SystemTime::now() - Duration::from_secs(3600));
        assert!(compute(i) < 90);
    }

    #[test]
    fn old_access_increases_score() {
        let mut i = input(90, Category::Temp);
        i.last_access = Some(SystemTime::now() - Duration::from_secs(90 * 86400));
        let c = compute(i);
        assert!(c > 90);
        assert!(c <= 100);
    }

    #[test]
    fn locked_and_system_attrs_heavily_penalize() {
        let mut i = input(99, Category::Temp);
        i.locked = true;
        i.system_attr = true;
        assert!(compute(i) <= 1);
    }

    #[test]
    fn medium_risk_reduces_score() {
        let base = compute(input(90, Category::WindowsUpdateCache));
        assert_eq!(base, 82);
    }

    #[test]
    fn score_is_clamped_to_100() {
        let mut i = input(100, Category::Temp);
        i.last_access = Some(SystemTime::now() - Duration::from_secs(400 * 86400));
        assert_eq!(compute(i), 100);
    }

    #[test]
    fn action_safe_high_confidence_is_delete() {
        assert_eq!(action_for(97, RiskLevel::Safe), CandidateAction::Delete);
        assert_eq!(action_for(95, RiskLevel::Safe), CandidateAction::Delete);
    }

    #[test]
    fn action_safe_mid_confidence_is_review() {
        assert_eq!(action_for(85, RiskLevel::Safe), CandidateAction::Review);
    }

    #[test]
    fn action_safe_low_confidence_is_keep() {
        assert_eq!(action_for(40, RiskLevel::Safe), CandidateAction::Keep);
    }

    #[test]
    fn action_medium_never_deletes() {
        assert_eq!(action_for(99, RiskLevel::Medium), CandidateAction::Review);
        assert_eq!(action_for(50, RiskLevel::Medium), CandidateAction::Keep);
    }

    #[test]
    fn action_advanced_always_keeps() {
        assert_eq!(action_for(100, RiskLevel::Advanced), CandidateAction::Keep);
    }

    #[test]
    fn recommendation_ranges() {
        assert_eq!(recommendation(100), "Delete");
        assert_eq!(recommendation(80), "Probably safe");
        assert_eq!(recommendation(50), "Needs review");
        assert_eq!(recommendation(10), "Do not touch");
    }

    #[test]
    fn elapsed_days_is_zero_for_future_timestamps() {
        let future = SystemTime::now() + Duration::from_secs(60);
        assert_eq!(elapsed_days(future), Some(0));
    }

    #[test]
    fn epoch_is_many_days_ago() {
        assert!(elapsed_days(UNIX_EPOCH).unwrap() > 20000);
    }
}
