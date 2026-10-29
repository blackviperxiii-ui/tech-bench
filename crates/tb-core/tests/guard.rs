//! The send guard: every frame Tech Bench builds passes, and DEF/SCR resets, calibration or
//! parameter writes, service routines and anything else off the list are refused. Literal
//! refusal text, so a loosened guard shows up as a failing test.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::clear::*;
use tb_core::j1939::guard::{check, SendPolicy};

const SAFE_OFF: SendPolicy = SendPolicy {
    allow_tsc_hold: false,
};
const SAFE_ON: SendPolicy = SendPolicy {
    allow_tsc_hold: true,
};

fn refused(t: &J1939Tx, p: SendPolicy) -> String {
    check(t, p).expect_err("should be refused").0
}

#[test]
fn every_frame_the_app_builds_passes() {
    for pgn in [
        DM1, DM2, DM3, DM11, DEF_TANK, 0xFEEC, 0xFEDA, 0xFEEB, 0xFEE5,
    ] {
        for dest in [0u8, 17, 48, 61, 163, BROADCAST] {
            assert_eq!(
                check(&request_tx(pgn, dest), SAFE_OFF),
                Ok(()),
                "Request for 0x{pgn:04X} to SA {dest}"
            );
        }
    }
    for t in refresh_dm_frames(&destinations(17, &[61]))
        .iter()
        .chain(aftertreatment_read_frames(&[0, 163, 255]).iter())
        .chain(clear_request_frames(DM11, &[0, 48, 255]).iter())
        .chain(clear_request_frames(DM3, &[0, 48, 255]).iter())
    {
        assert_eq!(check(t, SAFE_OFF), Ok(()), "{t:?}");
    }
    assert_eq!(
        check(&tsc1_tx(0, 17), SAFE_OFF),
        Ok(()),
        "TSC1 release with Safety off"
    );
    assert_eq!(
        check(&tsc1_tx(1200, 17), SAFE_ON),
        Ok(()),
        "TSC1 hold with Safety on"
    );
    assert_eq!(
        check(&dm13_tx(true), SAFE_OFF),
        Ok(()),
        "DM13 stop broadcast"
    );
    assert_eq!(
        check(&dm13_tx(false), SAFE_OFF),
        Ok(()),
        "DM13 start broadcast"
    );
    assert_eq!(
        check(&address_claim_tx(), SAFE_OFF),
        Ok(()),
        "own address claim"
    );
}

#[test]
fn tsc1_hold_needs_the_safety_toggle() {
    assert_eq!(
        refused(&tsc1_tx(1200, 0), SAFE_OFF),
        "refused: TSC1 hold while the Safety toggle is off"
    );
    let mut torque = tsc1_tx(1200, 0);
    torque.data[0] = 0x02; // torque control
    torque.rp1210 = rp1210_message(torque.pgn, torque.dest, &torque.data, torque.priority);
    assert_eq!(
        refused(&torque, SAFE_ON),
        "refused: TSC1 control mode 2; only release and speed control are sent"
    );
}

#[test]
fn calibration_and_parameter_writes_are_refused() {
    // DM14 memory access request: the J1939 way to write calibration or parameters.
    let dm14 = tx(
        0xD900,
        0,
        &[0x01, 0x13, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF],
        6,
    );
    assert_eq!(
        refused(&dm14, SAFE_ON),
        "refused: PGN 0xD900 (DM14 memory access (calibration / parameter write))"
    );
    let dm16 = tx(0xD700, 0, &[0x07, 1, 2, 3, 4, 5, 6, 7], 6);
    assert_eq!(
        refused(&dm16, SAFE_ON),
        "refused: PGN 0xD700 (DM16 binary data transfer (calibration / parameter write))"
    );
    let dm7 = tx(
        0xE300,
        0,
        &[0xF7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        6,
    );
    assert_eq!(
        refused(&dm7, SAFE_ON),
        "refused: PGN 0xE300 (DM7 test command)"
    );
    let cmd_addr = tx(0xFED8, 255, &[0; 9], 6);
    assert_eq!(
        refused(&cmd_addr, SAFE_ON),
        "refused: PGN 0xFED8 (commanded address)"
    );
}

#[test]
fn def_scr_resets_and_service_routines_are_refused() {
    // OEM DEF/SCR resets and forced regens go over proprietary PGNs: A (0xEF00) and B (0xFFxx).
    let prop_a = tx(0xEF00, 0, &[0x55, 0, 0, 0, 0, 0, 0, 0], 6);
    assert_eq!(
        refused(&prop_a, SAFE_ON),
        "refused: PGN 0xEF00 (proprietary A (OEM service routine))"
    );
    for pgn in [0xFF00, 0xFF5A, 0xFFFF] {
        assert_eq!(
            refused(&tx(pgn, 255, &[0; 8], 6), SAFE_ON),
            format!("refused: PGN 0x{pgn:04X} (proprietary B (OEM service routine))")
        );
    }
    // Sending the DEF tank PGN as data (not asking for it) is refused too.
    assert_eq!(
        refused(&tx(DEF_TANK, 0, &[0xFA; 8], 6), SAFE_ON),
        "refused: PGN 0xFE56 (not on the send list)"
    );
    // DM11 / DM3 sent as data instead of requested.
    assert_eq!(
        refused(&tx(DM11, 0, &[0xFF; 8], 6), SAFE_ON),
        "refused: PGN 0xFED3 (not on the send list)"
    );
}

#[test]
fn requests_off_the_list_are_refused() {
    assert_eq!(
        refused(&request_tx(0xD900, 0), SAFE_ON),
        "refused: Request for PGN 0xD900 (DM14 memory access (calibration / parameter write)); Tech Bench only requests DM1/DM2/DM3/DM11, DEF tank and identity"
    );
    assert_eq!(
        refused(&request_tx(0xFF10, 0), SAFE_ON),
        "refused: Request for PGN 0xFF10 (proprietary B (OEM service routine)); Tech Bench only requests DM1/DM2/DM3/DM11, DEF tank and identity"
    );
    let mut long = request_tx(DM1, 0);
    long.data.push(0);
    long.rp1210 = rp1210_message(long.pgn, long.dest, &long.data, long.priority);
    assert_eq!(
        refused(&long, SAFE_ON),
        "refused: a Request carries exactly 3 bytes"
    );
}

#[test]
fn mismatched_or_spoofed_frames_are_refused() {
    // The bytes that go to the driver must be the frame that was checked.
    let mut swapped = request_tx(DM1, 0);
    swapped.rp1210 = rp1210_message(0xD900, 0, &[0; 8], 6);
    assert_eq!(
        refused(&swapped, SAFE_ON),
        "refused: frame bytes do not match the frame"
    );
    let other_name = tx(ADDRESS_CLAIM, BROADCAST, &[1, 2, 3, 4, 5, 0x00, 0, 0x80], 6);
    assert_eq!(
        refused(&other_name, SAFE_ON),
        "refused: address claim with a NAME that is not this tool's"
    );
    let dm13_directed = tx(
        DM13,
        0,
        &[0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        6,
    );
    assert_eq!(
        refused(&dm13_directed, SAFE_ON),
        "refused: DM13 other than global stop / start broadcast"
    );
}
