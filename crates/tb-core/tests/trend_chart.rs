//! The Trend tab's chart model, from `TrendChart.OnPaint` in src/Features/Adapters/TrendChart.cs.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::trend::{TrendChannel, TrendChart, TrendSample};

fn at(sec: i64, rpm: f64, red: bool, amber: bool) -> TrendSample {
    TrendSample {
        time: 1_700_000_000_000 + sec * 1000,
        rpm,
        red,
        amber,
        ..TrendSample::default()
    }
}

#[test]
fn empty_and_unpublished_plots_say_why() {
    let c = TrendChart::new(&[at(0, 800.0, false, false)], TrendChannel::Rpm);
    assert_eq!(c.notice, "Connect and let it run — one sample a second.");
    let two = [at(0, 800.0, false, false), at(1, 800.0, false, false)];
    let c = TrendChart::new(&two, TrendChannel::Oil);
    assert_eq!(c.notice, "Oil kPa was never published on this hookup.");
    assert!(c.heights.is_empty());
}

#[test]
fn line_ticks_bands_and_gaps() {
    let s = [
        at(0, 600.0, false, false),
        at(1, f64::NAN, false, true),
        at(2, 1600.0, true, true),
    ];
    let c = TrendChart::new(&s, TrendChannel::Rpm);
    assert_eq!(c.notice, "");
    assert_eq!(c.label, "RPM");
    // 600..1600 padded by 10% each way: 500..1700.
    assert_eq!(c.ticks, vec!["1700", "1400", "1100", "800", "500"]);
    assert_eq!(c.heights.len(), 3);
    assert!((c.heights[0].unwrap() - 100.0 / 1200.0).abs() < 1e-12);
    assert_eq!(c.heights[1], None, "a gap is never joined");
    assert!((c.heights[2].unwrap() - 1100.0 / 1200.0).abs() < 1e-12);
    assert_eq!(c.lamps, vec![0, 1, 2], "red wins over amber");
    assert_eq!(c.span, "2s  ·  3 samples");
    assert_eq!(c.last - c.first, 2000);
}

#[test]
fn a_flat_line_gets_a_band_and_small_values_one_decimal() {
    let mut a = at(0, 0.0, false, false);
    a.battery_v = 13.8;
    let mut b = at(1, 0.0, false, false);
    b.battery_v = 13.8;
    let c = TrendChart::new(&[a, b], TrendChannel::Battery);
    // 12.8..14.8 padded by 0.2: 12.6..15.0.
    assert_eq!(c.ticks, vec!["15.0", "14.4", "13.8", "13.2", "12.6"]);
    assert!((c.heights[0].unwrap() - 0.5).abs() < 1e-12);
}
