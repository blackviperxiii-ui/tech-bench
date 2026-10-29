//! Ported from SelfTest.cs BamTests, plus the stale-transfer timeout the C# reads off the clock.

// Under wasm-pack the same cases run through wasm-bindgen-test.
#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::{BamAssembler, J1939Frame};

fn cm(sa: i32, size: usize, pkts: u8, pgn: i32) -> J1939Frame {
    J1939Frame {
        pgn: 0xEC00,
        sa,
        da: 255,
        data: vec![
            0x20,
            (size & 0xFF) as u8,
            (size >> 8) as u8,
            pkts,
            0xFF,
            (pgn & 0xFF) as u8,
            ((pgn >> 8) & 0xFF) as u8,
            ((pgn >> 16) & 0xFF) as u8,
        ],
    }
}

fn dt(sa: i32, seq: u8, payload: &[u8]) -> J1939Frame {
    let mut d = vec![0xFF; 8];
    d[0] = seq;
    for (i, b) in payload.iter().take(7).enumerate() {
        d[i + 1] = *b;
    }
    J1939Frame {
        pgn: 0xEB00,
        sa,
        da: 255,
        data: d,
    }
}

#[test]
fn happy_path_two_packets() {
    let mut bam = BamAssembler::new();
    assert!(
        bam.feed(&cm(0, 12, 2, 0xFECA), 0).is_none(),
        "TP.CM alone is not a message"
    );
    assert!(
        bam.feed(&dt(0, 1, &[1, 2, 3, 4, 5, 6, 7]), 10).is_none(),
        "first TP.DT incomplete"
    );
    let out = bam
        .feed(&dt(0, 2, &[8, 9, 10, 11, 12]), 20)
        .expect("second TP.DT completes");
    assert_eq!(format!("{:04X}", out.pgn), "FECA");
    assert_eq!(out.data.len(), 12);
    assert_eq!(out.data[11], 12);
}

#[test]
fn duplicate_sequence_does_not_complete() {
    let mut bam = BamAssembler::new();
    bam.feed(&cm(0, 12, 2, 0xFECA), 0);
    bam.feed(&dt(0, 1, &[1, 2, 3, 4, 5, 6, 7]), 0);
    assert!(
        bam.feed(&dt(0, 1, &[1, 2, 3, 4, 5, 6, 7]), 0).is_none(),
        "emitted a truncated message"
    );
    assert!(
        bam.feed(&dt(0, 2, &[8, 9, 10, 11, 12]), 0).is_some(),
        "never completed"
    );
}

#[test]
fn out_of_range_sequence_ignored() {
    let mut bam = BamAssembler::new();
    bam.feed(&cm(0, 12, 2, 0xFECA), 0);
    assert!(
        bam.feed(&dt(0, 0, &[1, 2, 3]), 0).is_none(),
        "accepted seq 0"
    );
    assert!(
        bam.feed(&dt(0, 9, &[1, 2, 3]), 0).is_none(),
        "accepted seq 9"
    );
}

#[test]
fn single_frame_passes_through() {
    let mut bam = BamAssembler::new();
    let plain = J1939Frame {
        pgn: 0xF004,
        sa: 0,
        da: 255,
        data: vec![0; 8],
    };
    assert_eq!(bam.feed(&plain, 0), Some(plain.clone()));
}

#[test]
fn orphan_tp_dt_ignored() {
    let mut bam = BamAssembler::new();
    assert!(bam.feed(&dt(0, 1, &[1, 2, 3]), 0).is_none());
}

#[test]
fn stalled_transfer_dropped_after_two_seconds() {
    let mut bam = BamAssembler::new();
    bam.feed(&cm(0, 12, 2, 0xFECA), 0);
    bam.feed(&dt(0, 1, &[1, 2, 3, 4, 5, 6, 7]), 100);
    // 2.1 s after the last packet the transfer is gone, so packet 2 is an orphan.
    assert!(bam.feed(&dt(0, 2, &[8, 9, 10, 11, 12]), 2200).is_none());

    let mut bam = BamAssembler::new();
    bam.feed(&cm(0, 12, 2, 0xFECA), 0);
    bam.feed(&dt(0, 1, &[1, 2, 3, 4, 5, 6, 7]), 100);
    // Exactly 2.0 s later is still in time.
    assert!(bam.feed(&dt(0, 2, &[8, 9, 10, 11, 12]), 2100).is_some());
}
