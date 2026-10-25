//! UTC timestamps in .NET round-trip format (`DateTime.UtcNow.ToString("o")`), e.g.
//! `2026-05-01T14:30:00.0000000Z`, without a date crate.

use std::time::{SystemTime, UNIX_EPOCH};

/// Format a time given as 100 ns ticks since the Unix epoch.
pub fn round_trip_from_ticks(ticks: u64) -> String {
    let secs = ticks / 10_000_000;
    let frac = ticks % 10_000_000;
    let days = (secs / 86_400) as i64;
    let rem = secs % 86_400;
    let (y, m, d) = civil_from_days(days);
    format!(
        "{y:04}-{m:02}-{d:02}T{:02}:{:02}:{:02}.{frac:07}Z",
        rem / 3600,
        (rem / 60) % 60,
        rem % 60
    )
}

/// The current UTC time in round-trip format. On targets without a clock (bare wasm) this
/// returns the Unix epoch rather than panicking.
pub fn utc_now_o() -> String {
    let ticks = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| (d.as_nanos() / 100) as u64)
        .unwrap_or(0);
    round_trip_from_ticks(ticks)
}

/// Howard Hinnant's days-from-civil inverse: days since 1970-01-01 to (year, month, day).
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_known_instants() {
        assert_eq!(round_trip_from_ticks(0), "1970-01-01T00:00:00.0000000Z");
        // 2026-05-01T14:30:00Z = 1777645800 s
        assert_eq!(
            round_trip_from_ticks(1_777_645_800 * 10_000_000),
            "2026-05-01T14:30:00.0000000Z"
        );
        // 2024-02-29T23:59:59.1234567Z (leap day) = 1709251199 s
        assert_eq!(
            round_trip_from_ticks(1_709_251_199 * 10_000_000 + 1_234_567),
            "2024-02-29T23:59:59.1234567Z"
        );
    }

    #[test]
    fn now_has_round_trip_shape() {
        let s = utc_now_o();
        assert_eq!(s.len(), 28, "{s}");
        assert!(
            s.ends_with('Z') && &s[10..11] == "T" && &s[19..20] == ".",
            "{s}"
        );
    }
}
