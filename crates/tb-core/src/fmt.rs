//! Number formatting that matches the C# app's `double.ToString("0.0")` on .NET Framework 4:
//! the value is first cut to 15 significant digits, then rounded half away from zero.
//! Rust's `{:.1}` rounds the exact binary value half-to-even, which can differ in the last
//! digit (e.g. 0.25 -> "0.2" vs .NET "0.3"), so decoded strings would drift from the WinForms app.

/// Format `v` with exactly `places` digits after the decimal point, .NET Framework style.
pub fn fixed(v: f64, places: usize) -> String {
    if v.is_nan() {
        return "NaN".to_string();
    }
    if v.is_infinite() {
        return if v > 0.0 {
            "Infinity".to_string()
        } else {
            "-Infinity".to_string()
        };
    }
    let negative = v < 0.0;
    // 15 significant digits: d.dddddddddddddde<exp>
    let sci = format!("{:.14e}", v.abs());
    let (mant, exp) = sci.split_once('e').expect("{:e} always has an exponent");
    let exp: i32 = exp.parse().expect("{:e} exponent is an integer");
    let mut digits: Vec<u8> = mant
        .bytes()
        .filter(u8::is_ascii_digit)
        .map(|b| b - b'0')
        .collect();
    // `digits` reads as d0.d1d2... x 10^exp, so the integer part has exp+1 digits.
    let mut int_len = exp + 1;
    if v == 0.0 {
        digits = vec![0];
        int_len = 1;
    }
    let keep = int_len + places as i32;
    if keep < 0 {
        digits = vec![0];
        int_len = 1;
    } else if (keep as usize) < digits.len() {
        let round_up = digits[keep as usize] >= 5;
        digits.truncate(keep as usize);
        if round_up {
            let mut i = digits.len();
            loop {
                if i == 0 {
                    digits.insert(0, 1);
                    int_len += 1;
                    break;
                }
                i -= 1;
                if digits[i] == 9 {
                    digits[i] = 0;
                } else {
                    digits[i] += 1;
                    break;
                }
            }
        }
    }
    // Pad so there is at least one integer digit and `places` fraction digits.
    while int_len < 1 {
        digits.insert(0, 0);
        int_len += 1;
    }
    while digits.len() < int_len as usize + places {
        digits.push(0);
    }
    let int_len = int_len as usize;
    let mut int_part: String = digits[..int_len]
        .iter()
        .map(|d| (b'0' + d) as char)
        .collect();
    let trimmed = int_part.trim_start_matches('0');
    int_part = if trimmed.is_empty() {
        "0".to_string()
    } else {
        trimmed.to_string()
    };
    let frac: String = digits[int_len..int_len + places]
        .iter()
        .map(|d| (b'0' + d) as char)
        .collect();
    let all_zero = int_part == "0" && frac.bytes().all(|b| b == b'0');
    let sign = if negative && !all_zero { "-" } else { "" };
    if places == 0 {
        format!("{sign}{int_part}")
    } else {
        format!("{sign}{int_part}.{frac}")
    }
}

#[cfg(test)]
mod tests {
    use super::fixed;

    #[test]
    fn rounds_half_away_from_zero_like_net_framework() {
        assert_eq!(fixed(0.25, 1), "0.3");
        assert_eq!(fixed(0.35, 1), "0.4");
        assert_eq!(fixed(-0.25, 1), "-0.3");
        assert_eq!(fixed(2.675, 2), "2.68");
    }

    #[test]
    fn plain_values() {
        assert_eq!(fixed(50.0, 1), "50.0");
        assert_eq!(fixed(1234.5, 1), "1234.5");
        assert_eq!(fixed(27.6, 2), "27.60");
        assert_eq!(fixed(0.0, 1), "0.0");
        assert_eq!(fixed(9.96, 1), "10.0");
        assert_eq!(fixed(99.95, 1), "100.0");
        assert_eq!(fixed(0.04, 1), "0.0");
        assert_eq!(fixed(-0.04, 1), "0.0");
        assert_eq!(fixed(800.0, 0), "800");
        assert_eq!(fixed(0.004, 1), "0.0");
    }
}
