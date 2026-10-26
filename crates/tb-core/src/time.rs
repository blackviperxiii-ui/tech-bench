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

/// Parse an ISO 8601 / round-trip timestamp to 100 ns ticks since the Unix epoch (UTC).
/// Accepts `YYYY-MM-DD`, an optional `T` or space time `HH:MM[:SS[.fffffff]]`, and an optional
/// `Z` or `±HH:MM` offset; no offset means UTC (`DateTimeStyles.AssumeUniversal`). `None` for
/// anything else, which callers treat as `DateTime.MinValue`.
pub fn parse_utc_ticks(raw: &str) -> Option<i64> {
    let s = raw.trim();
    let b = s.as_bytes();
    let num = |from: usize, len: usize| -> Option<i64> {
        let part = s.get(from..from + len)?;
        part.bytes()
            .all(|c| c.is_ascii_digit())
            .then(|| part.parse().ok())?
    };
    let y = num(0, 4)?;
    if b.get(4) != Some(&b'-') || b.get(7) != Some(&b'-') {
        return None;
    }
    let mo = num(5, 2)?;
    let d = num(8, 2)?;
    if !(1..=12).contains(&mo) || d < 1 || d > days_in_month(y, mo) {
        return None;
    }
    let mut i = 10;
    let (mut hh, mut mi, mut ss, mut frac) = (0i64, 0i64, 0i64, 0i64);
    if matches!(b.get(i), Some(b'T') | Some(b' ')) {
        hh = num(i + 1, 2)?;
        if b.get(i + 3) != Some(&b':') {
            return None;
        }
        mi = num(i + 4, 2)?;
        i += 6;
        if b.get(i) == Some(&b':') {
            ss = num(i + 1, 2)?;
            i += 3;
            if b.get(i) == Some(&b'.') {
                i += 1;
                let start = i;
                while i < b.len() && b[i].is_ascii_digit() {
                    i += 1;
                }
                let digits = &s[start..i];
                if digits.is_empty() {
                    return None;
                }
                let mut padded = digits.chars().take(7).collect::<String>();
                while padded.len() < 7 {
                    padded.push('0');
                }
                frac = padded.parse().ok()?;
            }
        }
        if hh > 23 || mi > 59 || ss > 59 {
            return None;
        }
    }
    let mut offset_min = 0i64;
    match b.get(i) {
        None => {}
        Some(b'Z') if i + 1 == b.len() => {}
        Some(&sign @ (b'+' | b'-')) if i + 6 == b.len() && b[i + 3] == b':' => {
            let m = num(i + 1, 2)? * 60 + num(i + 4, 2)?;
            offset_min = if sign == b'+' { m } else { -m };
        }
        _ => return None,
    }
    let secs = days_from_civil(y, mo, d) * 86_400 + hh * 3600 + mi * 60 + ss - offset_min * 60;
    Some(secs * 10_000_000 + frac)
}

fn days_in_month(y: i64, m: i64) -> i64 {
    match m {
        2 if (y % 4 == 0 && y % 100 != 0) || y % 400 == 0 => 29,
        2 => 28,
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

/// Days since 1970-01-01 for a civil date (Howard Hinnant's days_from_civil).
fn days_from_civil(y: i64, m: i64, d: i64) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y - era * 400;
    let mp = (m + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
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
    fn parses_round_trip_and_offsets() {
        assert_eq!(parse_utc_ticks("1970-01-01T00:00:00.0000000Z"), Some(0));
        assert_eq!(
            parse_utc_ticks("2026-05-01T14:30:00.0000000Z"),
            Some(1_777_645_800 * 10_000_000)
        );
        assert_eq!(
            parse_utc_ticks("2024-02-29T23:59:59.1234567Z"),
            Some(1_709_251_199 * 10_000_000 + 1_234_567)
        );
        assert_eq!(
            parse_utc_ticks("2026-05-01T16:30:00+02:00"),
            Some(1_777_645_800 * 10_000_000)
        );
        assert_eq!(
            parse_utc_ticks("2026-05-01 14:30"),
            Some(1_777_645_800 * 10_000_000)
        );
        assert_eq!(
            parse_utc_ticks("2026-05-01"),
            Some(1_777_593_600 * 10_000_000)
        );
        assert_eq!(parse_utc_ticks(""), None);
        assert_eq!(parse_utc_ticks("yesterday"), None);
        assert_eq!(parse_utc_ticks("2023-02-29T00:00:00Z"), None);
        assert_eq!(parse_utc_ticks("2026-05-01T14:30:00Zjunk"), None);
        let now = utc_now_o();
        assert_eq!(
            parse_utc_ticks(&now).map(|t| round_trip_from_ticks(t as u64)),
            Some(now)
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
