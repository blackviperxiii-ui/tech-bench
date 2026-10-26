//! Ported from SelfTest.cs "J1939 DM11/DM3 code clear" (160): ClearCodesTests, ClearRunTests and
//! DefSourceTests, with the same scripted ECMs (SimModule / SimLink) and literal expected values.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::clear::*;
use tb_core::j1939::code_clear::*;
use tb_core::j1939::decode::normalize_pgn;
use tb_core::j1939::monitor::BusMonitor;
use tb_core::j1939::J1939Frame;

fn dest_list(engine_sa: i32, modules: &[i32]) -> String {
    destinations(engine_sa, modules)
        .iter()
        .map(u8::to_string)
        .collect::<Vec<_>>()
        .join(",")
}

/// DM1/DM2 payload: lamps, reserved, then SPN/FMI/occ per code (or the empty pattern).
fn dm_frame(pgn: i32, sa: i32, lamps: u8, codes: &[(i32, i32)]) -> J1939Frame {
    let mut data = vec![lamps, 0xFF];
    if codes.is_empty() {
        data.extend_from_slice(&[0, 0, 0, 0, 0xFF, 0xFF]);
    } else {
        for &(spn, fmi) in codes {
            data.push((spn & 0xFF) as u8);
            data.push(((spn >> 8) & 0xFF) as u8);
            data.push(((((spn >> 16) & 0x07) << 5) | (fmi & 0x1F)) as u8);
            data.push(1);
        }
    }
    while data.len() < 8 {
        data.push(0xFF);
    }
    J1939Frame {
        pgn,
        sa,
        da: 255,
        data,
    }
}

fn codes(spn_fmi: &[i32]) -> Vec<(i32, i32)> {
    spn_fmi.chunks(2).map(|p| (p[0], p[1])).collect()
}

/// One ECM on the simulated bus: what it holds and how it answers a clear.
#[derive(Clone)]
struct SimModule {
    sa: i32,
    /// ACK control for a directed DM11 / DM3; -1 = stays silent.
    dm11_control: i32,
    dm3_control: i32,
    active: Vec<(i32, i32)>,
    prev: Vec<(i32, i32)>,
    /// Codes that are still true, so they re-latch right after a DM11.
    still_true: Vec<(i32, i32)>,
    answers_dm1: bool,
    answers_dm2: bool,
    def: Option<Vec<u8>>,
    def_after_clear: Option<Vec<u8>>,
    /// Sends some frame every pump step, so it counts as on the bus.
    talks: bool,
}

impl SimModule {
    fn lamps(&self) -> u8 {
        if self.active.iter().any(|c| c.0 == 5246) {
            0x10
        } else if !self.active.is_empty() {
            0x04
        } else {
            0x00
        }
    }
}

const SIM_START: i64 = 1_772_352_000_000; // 2026-03-01 08:00 UTC

/// ClearLink over scripted ECMs on a fake clock, capturing every frame like Rp1210.Capture.
struct SimLink {
    clock: i64,
    modules: Vec<SimModule>,
    sent: Vec<J1939Tx>,
    fail_sends: usize,
    drop_at_ms: i64,
    pumped_ms: i64,
    last_progress: String,
    pending: Vec<J1939Frame>,
}

impl SimLink {
    fn new() -> Self {
        Self {
            clock: SIM_START,
            modules: Vec::new(),
            sent: Vec::new(),
            fail_sends: 0,
            drop_at_ms: -1,
            pumped_ms: 0,
            last_progress: String::new(),
            pending: Vec::new(),
        }
    }

    fn add(&mut self, sa: i32) -> &mut SimModule {
        self.modules.push(SimModule {
            sa,
            dm11_control: ACK_POSITIVE,
            dm3_control: ACK_POSITIVE,
            active: Vec::new(),
            prev: Vec::new(),
            still_true: Vec::new(),
            answers_dm1: true,
            answers_dm2: true,
            def: None,
            def_after_clear: None,
            talks: true,
        });
        self.modules.last_mut().unwrap()
    }

    fn module(&mut self, sa: i32) -> &mut SimModule {
        self.modules.iter_mut().find(|m| m.sa == sa).unwrap()
    }

    fn chatter(sa: i32) -> J1939Frame {
        J1939Frame {
            pgn: 0xFEEE,
            sa,
            da: 255,
            data: vec![0; 8],
        }
    }

    /// Put what the modules hold onto the monitor, as a live hookup would have.
    fn prime(&mut self, mon: &mut BusMonitor) {
        for m in &self.modules {
            if m.talks {
                mon.feed(&Self::chatter(m.sa), self.clock);
            }
            if m.answers_dm1 {
                mon.feed(&dm_frame(DM1, m.sa, m.lamps(), &m.active), self.clock);
            }
            if m.answers_dm2 {
                mon.feed(&dm_frame(DM2, m.sa, 0, &m.prev), self.clock);
            }
            if let Some(d) = &m.def {
                mon.feed(
                    &J1939Frame {
                        pgn: DEF_TANK,
                        sa: m.sa,
                        da: 255,
                        data: d.clone(),
                    },
                    self.clock,
                );
            }
        }
        self.clock += 1000;
    }
}

impl ClearLink for SimLink {
    fn now(&self) -> i64 {
        self.clock
    }

    fn last_error(&self) -> String {
        "simulated reject".to_string()
    }

    fn progress(&mut self, what: &str) {
        self.last_progress = what.to_string();
    }

    fn send(&mut self, tx: &J1939Tx) -> bool {
        self.sent.push(tx.clone());
        if self.fail_sends > 0 {
            self.fail_sends -= 1;
            return false;
        }
        let req = tx.requested_pgn();
        if req < 0 {
            return true;
        }
        for m in &mut self.modules {
            let direct = i32::from(tx.dest) == m.sa;
            if !direct && tx.dest != BROADCAST {
                continue;
            }
            if req == DM11 || req == DM3 {
                let control = if req == DM11 {
                    m.dm11_control
                } else {
                    m.dm3_control
                };
                if control < 0 {
                    continue;
                }
                if control == ACK_POSITIVE {
                    if req == DM11 {
                        m.active = m.still_true.clone();
                        if let Some(d) = &m.def_after_clear {
                            m.def = Some(d.clone());
                        }
                    } else {
                        m.prev.clear();
                    }
                }
                // A global request is never acknowledged.
                if direct {
                    self.pending.push(ack_frame(
                        m.sa,
                        control,
                        req,
                        i32::from(BROADCAST),
                        i32::from(TOOL_SA),
                    ));
                }
            } else if req == DM1 && m.answers_dm1 {
                self.pending.push(dm_frame(DM1, m.sa, m.lamps(), &m.active));
            } else if req == DM2 && m.answers_dm2 {
                self.pending.push(dm_frame(DM2, m.sa, 0, &m.prev));
            } else if req == DEF_TANK {
                if let Some(d) = &m.def {
                    self.pending.push(J1939Frame {
                        pgn: DEF_TANK,
                        sa: m.sa,
                        da: 255,
                        data: d.clone(),
                    });
                }
            }
        }
        true
    }

    fn pump(
        &mut self,
        mon: &mut BusMonitor,
        ms: i64,
        done: Option<&dyn Fn(&BusMonitor) -> bool>,
    ) -> bool {
        const STEP: i64 = 25;
        let mut t = 0;
        while t < ms {
            self.clock += STEP;
            self.pumped_ms += STEP;
            if self.drop_at_ms >= 0 && self.pumped_ms >= self.drop_at_ms {
                return false;
            }
            for f in self.pending.drain(..) {
                mon.feed(&f, self.clock);
            }
            for m in &self.modules {
                if m.talks {
                    mon.feed(&Self::chatter(m.sa), self.clock);
                }
            }
            if let Some(d) = done {
                if d(mon) {
                    return true;
                }
            }
            t += STEP;
        }
        true
    }
}

fn lf(s: &str) -> String {
    s.replace("\r\n", "\n")
}

#[test]
fn clear_frames_and_acks() {
    assert_eq!(
        dest_list(0, &[]),
        "0,48,255",
        "clear dests cover engine + compressor + broadcast"
    );
    assert_eq!(
        dest_list(17, &[]),
        "0,17,48,255",
        "clear dests include detected engine SA 17"
    );
    assert_eq!(
        dest_list(48, &[]),
        "0,48,255",
        "compressor as engine SA is not duplicated"
    );
    assert_eq!(
        dest_list(-1, &[]),
        "0,48,255",
        "unknown engine SA still hits 0 and 48"
    );
    assert_eq!(
        dest_list(255, &[]),
        "0,48,255",
        "broadcast engine SA rejected from dest list"
    );
    assert_eq!(
        dest_list(0, &[61, 48, 0]),
        "0,48,61,255",
        "modules reporting faults get their own clear"
    );
    assert_eq!(
        dest_list(0, &[249, 254, 255, 300, -4]),
        "0,48,255",
        "tool, null, global, and junk SAs are not module dests"
    );
    assert_eq!(
        directed(&destinations(17, &[61])),
        vec![0, 17, 48, 61],
        "directed part drops global"
    );

    // DM11 is a Request (PGN 59904) carrying the DM11 PGN, never DM11 sent as data.
    let req = request_tx(DM11, COMPRESSOR_SA);
    assert_eq!(
        req.rp1210,
        vec![0x00, 0xEA, 0x00, 6, 0xF9, 48, 0xD3, 0xFE, 0x00],
        "request wire: PGN EA00, priority 6, tool SA F9, dest 48, payload DM11"
    );
    assert_eq!(req.requested_pgn(), DM11, "requested PGN read back");

    assert_eq!(
        normalize_pgn(0xE8F9),
        0xE800,
        "PDU1 PGN with dest byte normalized (ACK)"
    );
    assert_eq!(
        normalize_pgn(0xECFF),
        0xEC00,
        "PDU1 PGN with dest byte normalized (TP.CM)"
    );
    assert_eq!(normalize_pgn(0xFECA), 0xFECA, "PDU2 PGN kept (DM1)");
    assert_eq!(normalize_pgn(0x1E8F9), 0x1E800, "data page kept");

    let t0 = SIM_START;
    let ack = try_parse_ack(&ack_frame(0, 0, DM11, 255, 0xF9), t0).expect("ACK parsed");
    assert_eq!(ack.control, ACK_POSITIVE, "ACK control positive");
    assert_eq!(ack.pgn, DM11, "ACK names DM11");
    assert!(ack.for_tool(), "ACK to global with tool address is ours");
    let nack = try_parse_ack(&ack_frame(0, 1, DM3, 0xF9, 0xFF), t0).unwrap();
    assert!(nack.for_tool(), "NACK sent to the tool is ours");
    assert_eq!(nack.control, ACK_NEGATIVE, "NACK control");
    let other = try_parse_ack(&ack_frame(0, 0, DM11, 255, 0xFA), t0).unwrap();
    assert!(!other.for_tool(), "ACK for another tool (SA FA) ignored");
    let old = try_parse_ack(&ack_frame(0, 0, DM11, 255, 0xFF), t0).unwrap();
    assert!(old.for_tool(), "pre-2006 ACK (address FF) accepted");
    assert!(
        try_parse_ack(
            &J1939Frame {
                pgn: 0xE800,
                sa: 0,
                da: 255,
                data: vec![0; 3]
            },
            t0
        )
        .is_none(),
        "short ACK rejected"
    );

    let mut mon = BusMonitor::new();
    let mut raw = ack_frame(0, 0, DM11, 0xF9, 0xF9);
    raw.pgn = 0xE8F9; // driver left the destination in the PGN
    mon.feed(&raw, t0);
    assert!(
        mon.ack_from(0, DM11, t0).is_some(),
        "monitor routes an un-normalized ACK"
    );
    assert!(
        mon.ack_from(0, DM11, t0 + 1).is_none(),
        "ACK lookup honors the since time"
    );
    assert!(mon.ack_from(0, DM3, t0).is_none(), "ACK lookup is per PGN");
    assert!(
        mon.ack_from(48, DM11, t0).is_none(),
        "ACK lookup is per module"
    );

    assert!(
        is_aftertreatment_spn(5246),
        "aftertreatment SPN 5246 flagged"
    );
    assert!(
        is_aftertreatment_spn(1761),
        "aftertreatment SPN 1761 flagged"
    );
    assert!(
        !is_aftertreatment_spn(110),
        "coolant SPN 110 not aftertreatment"
    );
}

const DEF_INDUCED: [u8; 8] = [125, 60, 0xFF, 0xFF, 1 << 5, 3 << 5, 0xFF, 0xFF];
const DEF_OK: [u8; 8] = [125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF];

fn run(kind: ClearKind, sim: &mut SimLink, mon: &mut BusMonitor) -> (CodeClearRun, String) {
    let mut r = CodeClearRun::new(kind);
    let report = r.execute(mon, sim);
    (r, report)
}

fn has(report: &str, what: &str) -> bool {
    report.contains(what)
}

#[test]
fn clear_1_repaired_machine() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    {
        let eng = sim.add(0);
        eng.active = codes(&[5246, 31, 1761, 9]);
        eng.prev = codes(&[100, 1, 3364, 9]);
        eng.def = Some(DEF_INDUCED.to_vec());
        eng.def_after_clear = Some(DEF_OK.to_vec());
    }
    sim.add(48).active = codes(&[520200, 3]);
    sim.prime(&mut mon);
    assert_eq!(mon.engine().active.len(), 2, "primed engine active codes");
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    let cap = &sim.sent;
    assert!(
        has(&report, "RESULT: cleared — no active codes came back."),
        "good clear headline\n{report}"
    );
    assert_eq!(
        r.target(0).unwrap().dm11,
        ClearAnswer::Ack,
        "engine DM11 answer"
    );
    assert_eq!(
        r.target(0).unwrap().dm3,
        ClearAnswer::Ack,
        "engine DM3 answer"
    );
    assert_eq!(
        r.target(48).unwrap().dm11,
        ClearAnswer::Ack,
        "compressor DM11 answer"
    );
    assert_eq!(
        count_request(cap, DM11, 0),
        1,
        "DM11 request to engine once (ACKed, no retry)"
    );
    assert_eq!(
        count_request(cap, DM11, 48),
        1,
        "DM11 request to compressor SA 48 once"
    );
    assert_eq!(
        count_request(cap, DM11, 255),
        1,
        "DM11 request to everyone once"
    );
    assert_eq!(count_request(cap, DM3, 0), 1, "DM3 request to engine once");
    assert_eq!(
        count_request(cap, DM3, 48),
        1,
        "DM3 request to compressor SA 48 once"
    );
    assert_eq!(
        count_request(cap, DM3, 255),
        1,
        "DM3 request to everyone once"
    );
    assert_eq!(count_pgn(cap, DM11), 0, "DM11 is never sent as data");
    assert_eq!(count_pgn(cap, DM3), 0, "DM3 is never sent as data");
    let first_dm3 = cap.iter().position(|x| x.requested_pgn() == DM3).unwrap();
    let last_dm11 = cap.iter().rposition(|x| x.requested_pgn() == DM11).unwrap();
    assert!(last_dm11 < first_dm3, "DM11 finishes before DM3 starts");
    assert_eq!(count_request(cap, DM1, 0), 1, "re-read DM1 from engine");
    assert_eq!(
        count_request(cap, DM1, 48),
        1,
        "re-read DM1 from compressor SA 48"
    );
    assert_eq!(count_request(cap, DM2, 0), 1, "re-read DM2 from engine");
    assert_eq!(count_request(cap, DM1, 255), 1, "re-read DM1 from everyone");
    assert_eq!(
        count_request(cap, DEF_TANK, 0),
        1,
        "re-read DEF tank from engine"
    );
    assert_eq!(
        count_request(cap, DEF_TANK, 255),
        1,
        "re-read DEF tank from everyone"
    );
    assert!(
        cap.iter()
            .all(|tx| rp1210_message(tx.pgn, tx.dest, &tx.data, tx.priority) == tx.rp1210),
        "captured RP1210 bytes match constructor"
    );
    assert!(
        cap.iter().all(|tx| tx.pgn == REQUEST),
        "a code clear only ever sends Requests"
    );
    assert_eq!(
        mon.engine().active.len(),
        0,
        "engine list empty after the fresh DM1"
    );
    assert_eq!(
        mon.engine().prev.len(),
        0,
        "engine previous list empty after the fresh DM2"
    );
    assert_eq!(
        mon.module(48).active.len(),
        0,
        "controller list empty after its fresh DM1"
    );
    assert!(
        has(&report, "Active: 2 before → 0 now"),
        "report shows engine went 2 → 0"
    );
    assert!(
        has(&report, "Previous: 2 before → 0 now"),
        "report shows previous 2 → 0"
    );
    assert!(
        has(&report, "Engine lamps now: Red Stop off"),
        "report reads engine lamps from the fresh DM1"
    );
    assert!(
        has(&report, "read after the clear"),
        "report shows the DEF/SCR side read after the clear"
    );
    assert!(
        has(&report, "SCR inducement SPN 5246: not active"),
        "report shows inducement now"
    );
    assert!(
        has(&report, "SCR inducement was: level 3"),
        "report shows inducement before"
    );
    assert!(
        !has(&report, "What to do:"),
        "good clear has no fix-it advice"
    );
    assert!(
        sim.pumped_ms < 2500,
        "good clear finished quickly: {} ms",
        sim.pumped_ms
    );
    assert_eq!(
        r.headline(),
        "RESULT: cleared — no active codes came back.",
        "headline kept"
    );
    assert!(
        report.ends_with("\r\n") && !report.ends_with("\r\n\r\n"),
        "report ends with one CRLF"
    );
    assert_eq!(
        lf(&report).lines().take(4).collect::<Vec<_>>(),
        vec![
            "CODE CLEAR — reset all codes (J1939 DM11 clear active + DM3 clear previous)",
            "RESULT: cleared — no active codes came back.",
            "",
            "Asked directly: SA 0, SA 48, then everyone (SA 255).",
        ],
        "report opening lines"
    );
    assert_eq!(
        sim.last_progress, "clearing codes — reading DM1 / DM2 / DEF back",
        "last progress line"
    );
}

#[test]
fn clear_2_fault_still_true() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    {
        let eng = sim.add(0);
        eng.active = codes(&[5246, 31, 1761, 9]);
        eng.still_true = codes(&[5246, 31]);
        eng.def = Some(DEF_INDUCED.to_vec());
    }
    sim.prime(&mut mon);
    let (_, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert!(
        has(
            &report,
            "RESULT: cleared — 1 active code came straight back"
        ),
        "came-back headline\n{report}"
    );
    assert!(
        has(&report, "still active: SPN 5246 FMI 31") && has(&report, "[DEF/SCR]"),
        "returning code listed and tagged"
    );
    assert!(
        has(&report, "Engine lamps now: Red Stop ON"),
        "Red Stop read from the fresh DM1"
    );
    assert!(
        has(&report, "The clear worked; the fault did not go away"),
        "came-back advice"
    );
    assert!(
        has(&report, "a code clear does not end SCR inducement"),
        "inducement advice"
    );
    assert!(
        has(&report, "never disables DEF/SCR"),
        "inducement advice never offers a delete"
    );
    assert_eq!(
        mon.engine().active.len(),
        1,
        "monitor keeps the re-latched code"
    );
}

#[test]
fn clear_3_engine_refuses() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    {
        let eng = sim.add(0);
        eng.active = codes(&[100, 1]);
        eng.dm11_control = ACK_DENIED;
        eng.dm3_control = ACK_DENIED;
    }
    sim.prime(&mut mon);
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert_eq!(
        r.target(0).unwrap().dm11,
        ClearAnswer::Denied,
        "denied answer recorded"
    );
    assert!(
        has(&report, "RESULT: REFUSED by SA 0"),
        "refused headline\n{report}"
    );
    assert!(has(&report, "key ON, engine OFF"), "refused advice");
    assert!(
        !has(&report, "The clear worked"),
        "a refused clear is not called a clear that worked"
    );
    assert_eq!(
        count_request(&sim.sent, DM11, 0),
        1,
        "a refusal is final (no retry)"
    );
}

#[test]
fn clear_4_controller_ignores_clears() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0).active = codes(&[100, 1]);
    {
        let comp = sim.add(48);
        comp.active = codes(&[520200, 3]);
        comp.still_true = codes(&[520200, 3]);
        comp.dm11_control = -1;
        comp.dm3_control = -1;
    }
    sim.prime(&mut mon);
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert_eq!(
        count_request(&sim.sent, DM11, 48),
        2,
        "silent controller retried once on DM11"
    );
    assert_eq!(
        count_request(&sim.sent, DM11, 0),
        1,
        "ACKed engine not retried"
    );
    assert_eq!(
        r.target(48).unwrap().dm11,
        ClearAnswer::NoAnswer,
        "silent controller answer"
    );
    assert!(
        has(
            &report,
            "RESULT: partly cleared; SA 48 did not answer and still shows codes."
        ),
        "silent controller headline\n{report}"
    );
    assert!(
        has(&report, "did not answer, even after a retry") && has(&report, "own keypad"),
        "silent controller advice"
    );
    assert!(
        !has(&report, "came straight back"),
        "unanswered code is not called a code that came back"
    );
    assert!(
        has(
            &lf(&report),
            "SA 48 Compressor controller\n  DM11 clear active: no answer"
        ),
        "silent controller line"
    );
}

#[test]
fn clear_5_controller_stops_sending_dm1() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0);
    sim.add(48).active = codes(&[520200, 3]);
    sim.prime(&mut mon);
    sim.module(48).answers_dm1 = false;
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert!(
        r.target(48).unwrap().emptied_active,
        "controller list emptied on ACK\n{report}"
    );
    assert_eq!(
        mon.module(48).active.len(),
        0,
        "monitor no longer shows the cleared controller code"
    );
    assert!(
        has(&report, "ACKed, no DM1 since; the list was emptied"),
        "emptied line explains why"
    );
}

#[test]
fn clear_6_adapter_refuses_every_frame() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0).active = codes(&[100, 1]);
    sim.prime(&mut mon);
    sim.fail_sends = 1000;
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert!(
        has(
            &report,
            "RESULT: FAILED — the adapter rejected every request (simulated reject)"
        ),
        "rejected headline\n{report}"
    );
    assert_eq!(sim.pumped_ms, 0, "rejected run did not wait for ACKs");
    assert_eq!(
        count_request(&sim.sent, DM3, 0),
        0,
        "rejected run stops before DM3"
    );
    assert_eq!(
        r.target(0).unwrap().dm11,
        ClearAnswer::SendFailed,
        "rejected answer"
    );
}

#[test]
fn clear_7_adapter_drops_mid_clear() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    {
        let eng = sim.add(0);
        eng.active = codes(&[100, 1]);
        eng.dm11_control = -1;
    }
    sim.prime(&mut mon);
    sim.drop_at_ms = 300;
    let (_, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert!(
        has(&report, "RESULT: INTERRUPTED"),
        "interrupted headline\n{report}"
    );
    assert!(
        !has(&report, "did not answer"),
        "interrupted run does not blame the engine for not answering"
    );
    assert_eq!(
        count_request(&sim.sent, DM3, 0),
        0,
        "interrupted run sends no DM3"
    );
    assert_eq!(
        mon.engine().active.len(),
        1,
        "interrupted run keeps the codes it could not confirm"
    );
}

#[test]
fn clear_8_clear_previous_only() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    {
        let eng = sim.add(0);
        eng.active = codes(&[100, 1]);
        eng.still_true = codes(&[100, 1]);
        eng.prev = codes(&[110, 0, 3364, 9]);
    }
    sim.prime(&mut mon);
    let (_, report) = run(ClearKind::Previous, &mut sim, &mut mon);
    assert_eq!(
        count_request(&sim.sent, DM11, 0) + count_request(&sim.sent, DM11, 255),
        0,
        "clear previous sends no DM11"
    );
    assert_eq!(
        count_request(&sim.sent, DM3, 0),
        1,
        "clear previous sends DM3 to the engine"
    );
    assert!(
        has(&report, "clear previously active codes (J1939 DM3)"),
        "clear previous title"
    );
    assert!(
        has(&report, "RESULT: previously active codes cleared."),
        "clear previous headline\n{report}"
    );
    assert!(
        has(&report, "Previous: 2 before → 0 now"),
        "clear previous shows 2 → 0"
    );
    assert_eq!(
        mon.engine().active.len(),
        1,
        "clear previous leaves active codes alone"
    );
}

#[test]
fn clear_9_compressor_not_fitted() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0).active = codes(&[100, 1]);
    sim.prime(&mut mon);
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert_eq!(
        count_request(&sim.sent, DM11, 48),
        1,
        "absent SA 48 still asked"
    );
    assert_eq!(
        count_request(&sim.sent, DM3, 48),
        1,
        "absent SA 48 not retried"
    );
    assert_eq!(
        r.target(48).unwrap().dm11,
        ClearAnswer::NotOnBus,
        "absent SA 48 answer"
    );
    assert!(
        has(&report, "SA 48 Compressor controller — not on the bus"),
        "absent SA 48 is one line\n{report}"
    );
    assert!(
        sim.pumped_ms < 2500,
        "absent SA 48 does not hold the clear up: {} ms",
        sim.pumped_ms
    );
}

#[test]
fn clear_10_separate_aftertreatment_module() {
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0);
    sim.add(61).active = codes(&[4364, 18]);
    sim.prime(&mut mon);
    let (r, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert_eq!(
        count_request(&sim.sent, DM11, 61),
        1,
        "aftertreatment module asked directly"
    );
    assert_eq!(
        r.target(61).unwrap().dm11,
        ClearAnswer::Ack,
        "aftertreatment module ACK"
    );
    assert!(
        has(&lf(&report), "SA 61\n  DM11 clear active: ACK (cleared)"),
        "aftertreatment module in report\n{report}"
    );
    assert!(
        has(&report, "no DEF tank message (PGN FE56) on this hookup"),
        "no FE56 on this bus is said plainly"
    );
}

#[test]
fn def_source() {
    let t = 1_772_355_600_000; // 2026-03-01 09:00 UTC
    let eec1 = J1939Frame {
        pgn: 0xF004,
        sa: 0,
        da: 255,
        data: vec![0, 0, 0, 0x00, 0x19, 0, 0, 0],
    };
    let fe56 = |sa: i32, level: u8| J1939Frame {
        pgn: 0xFE56,
        sa,
        da: 255,
        data: vec![level, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF],
    };
    let mut m = BusMonitor::new();
    m.feed(&eec1, t);
    // The tank header (here SA 163) sends FE56 itself; the ECM never does.
    m.feed(&fe56(163, 125), t);
    assert_eq!(m.def_pct, 50.0, "FE56 from a non-engine module used");
    assert_eq!(m.def_sa, 163, "DEF source remembered");
    assert!(
        m.aft_text().contains("FE56 from SA 163"),
        "panel says where FE56 came from"
    );
    m.feed(&fe56(0, 100), t + 1000);
    assert_eq!(m.def_pct, 40.0, "engine FE56 wins when it talks");
    assert_eq!(m.def_sa, 0, "DEF source switches to the engine");
    m.feed(&fe56(163, 250), t + 2000);
    assert_eq!(
        m.def_pct, 40.0,
        "other FE56 ignored while the engine is talking"
    );
    m.feed(&fe56(163, 250), t + 10_000);
    assert_eq!(m.def_pct, 100.0, "falls back once the engine goes quiet");

    let mut two = BusMonitor::new();
    two.feed(&fe56(163, 125), t);
    two.feed(&fe56(164, 25), t + 200);
    assert_eq!(
        two.def_pct, 50.0,
        "a second non-engine FE56 does not flicker the readout"
    );
    two.clear_live();
    assert_eq!(two.def_sa, -1, "clear live forgets the DEF source");

    // Clear re-reads FE56 from the module that actually sends it.
    let mut sim = SimLink::new();
    let mut mon = BusMonitor::new();
    sim.add(0);
    sim.add(163).def = Some(vec![125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF]);
    sim.prime(&mut mon);
    let (_, report) = run(ClearKind::All, &mut sim, &mut mon);
    assert_eq!(
        count_request(&sim.sent, DEF_TANK, 163),
        1,
        "clear re-reads FE56 from the tank header"
    );
    assert!(
        has(&report, "PGN FE56 from SA 163, read after the clear"),
        "report names the FE56 source\n{report}"
    );
}
