//! One J1939-73 code clear, start to finish (port of `src/Safety/CodeClear.cs`):
//!   1. note what every module says now (DM1/DM2, DEF/SCR),
//!   2. Request DM11 (reset all only) to each module directly, then to everyone, and wait for each
//!      module's ACK / NACK; one retry for a module on the bus that stayed quiet,
//!   3. the same for DM3,
//!   4. give the modules a second to re-check, re-request DM1/DM2/FE56, and wait for them,
//!   5. report per module what cleared, what came back, and what the DEF/SCR side shows.
//!
//! Every frame also passes [`super::guard::check`] before it reaches the link, so a clear can only
//! ever put Requests on the bus.

use super::bam::J1939Frame;
use super::clear::*;
use super::decode::{AftState, Dtc};
use super::guard::{self, SendPolicy};
use super::monitor::BusMonitor;
use super::trend::NEVER;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClearKind {
    /// DM11 (clear active) then DM3 (clear previously active).
    All,
    /// DM3 (clear previously active) only.
    Previous,
}

/// How one module answered a directed DM11 or DM3 request.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ClearAnswer {
    NotAsked,
    SendFailed,
    NotOnBus,
    NoAnswer,
    Ack,
    Nack,
    Denied,
    Busy,
}

/// The bus as a code clear sees it. The RP1210 bridge implements it over the live adapter and keeps
/// reading frames while the clear waits, so the ACKs and fresh DM1/DM2 land; tests implement it with
/// scripted ECMs on a fake clock.
pub trait ClearLink {
    /// Milliseconds since the Unix epoch.
    fn now(&self) -> i64;
    /// False when the adapter refused the frame.
    fn send(&mut self, tx: &J1939Tx) -> bool;
    /// Keep reading the bus into `mon` for up to `ms`, returning early once `done` (if any) is true.
    /// False if the adapter dropped or the tech pressed Disconnect.
    fn pump(
        &mut self,
        mon: &mut BusMonitor,
        ms: i64,
        done: Option<&dyn Fn(&BusMonitor) -> bool>,
    ) -> bool;
    fn progress(&mut self, what: &str);
    fn last_error(&self) -> String;
}

/// One module the clear asked directly, before and after.
#[derive(Debug, Clone)]
pub struct ClearTarget {
    pub sa: i32,
    pub on_bus: bool,
    pub dm11: ClearAnswer,
    pub dm3: ClearAnswer,
    pub had_dm1: bool,
    pub had_dm2: bool,
    pub active_before: Vec<Dtc>,
    pub prev_before: Vec<Dtc>,
    /// None until a DM1 arrives after the clear (or the list was emptied on an ACK).
    pub active_after: Option<Vec<Dtc>>,
    /// None until a DM2 arrives after the clear (or the list was emptied on an ACK).
    pub prev_after: Option<Vec<Dtc>>,
    pub emptied_active: bool,
    pub emptied_prev: bool,
    pub fresh_dm1: bool,
    pub red: bool,
    pub amber: bool,
    pub protect: bool,
    pub mil: bool,
}

impl ClearTarget {
    fn new(sa: i32, on_bus: bool) -> Self {
        Self {
            sa,
            on_bus,
            dm11: ClearAnswer::NotAsked,
            dm3: ClearAnswer::NotAsked,
            had_dm1: false,
            had_dm2: false,
            active_before: Vec::new(),
            prev_before: Vec::new(),
            active_after: None,
            prev_after: None,
            emptied_active: false,
            emptied_prev: false,
            fresh_dm1: false,
            red: false,
            amber: false,
            protect: false,
            mil: false,
        }
    }

    fn answer(&self, pgn: i32) -> ClearAnswer {
        if pgn == DM11 {
            self.dm11
        } else {
            self.dm3
        }
    }

    fn set_answer(&mut self, pgn: i32, a: ClearAnswer) {
        if pgn == DM11 {
            self.dm11 = a;
        } else {
            self.dm3 = a;
        }
    }
}

/// J1939-21 T3: how long a requester waits for the answer.
pub const ACK_WAIT_MS: i64 = 1250;
pub const RETRY_WAIT_MS: i64 = 750;
/// Time for the modules to re-check their faults before DM1/DM2 are read back.
pub const SETTLE_MS: i64 = 1000;
pub const REREAD_MS: i64 = 3000;
/// An address counts as on the bus if it sent anything this recently.
pub const ON_BUS_MS: i64 = 10_000;

#[derive(Debug, Clone)]
pub struct CodeClearRun {
    kind: ClearKind,
    targets: Vec<ClearTarget>,
    engine_sa: i32,
    started: i64,
    reread: i64,
    sent: usize,
    rejected: usize,
    reject_why: String,
    aborted: bool,
    abort_why: String,
    had_def: bool,
    def_fresh: bool,
    def_sa: i32,
    aft_before: AftState,
    aft_after: AftState,
    headline: String,
    sa_labels: Vec<(i32, String)>,
}

fn from_control(control: i32) -> ClearAnswer {
    match control {
        ACK_POSITIVE => ClearAnswer::Ack,
        ACK_NEGATIVE => ClearAnswer::Nack,
        ACK_DENIED => ClearAnswer::Denied,
        ACK_BUSY => ClearAnswer::Busy,
        _ => ClearAnswer::Nack,
    }
}

fn refused(a: ClearAnswer) -> bool {
    matches!(a, ClearAnswer::Nack | ClearAnswer::Denied)
}

pub fn answer_text(a: ClearAnswer) -> &'static str {
    match a {
        ClearAnswer::Ack => "ACK (cleared)",
        ClearAnswer::Nack => "NACK (refused)",
        ClearAnswer::Denied => "access denied",
        ClearAnswer::Busy => "busy (cannot respond)",
        ClearAnswer::NoAnswer => "no answer",
        ClearAnswer::NotOnBus => "not on the bus",
        ClearAnswer::SendFailed => "adapter rejected the request",
        ClearAnswer::NotAsked => "not asked",
    }
}

fn phase_name(pgn: i32) -> &'static str {
    if pgn == DM11 {
        "DM11 clear active"
    } else {
        "DM3 clear previous"
    }
}

fn all_answered(targets: &[ClearTarget], pgn: i32, since: i64, mon: &BusMonitor) -> bool {
    targets.iter().all(|t| {
        if !t.on_bus {
            return true;
        }
        let a = t.answer(pgn);
        if a != ClearAnswer::NoAnswer && a != ClearAnswer::Busy {
            return true;
        }
        mon.ack_from(t.sa, pgn, since).is_some()
    })
}

fn read_back_done(targets: &[ClearTarget], reread: i64, had_def: bool, mon: &BusMonitor) -> bool {
    for t in targets.iter().filter(|t| t.on_bus) {
        let m = mon.peek(t.sa);
        if t.had_dm1 && m.is_none_or(|m| m.last_dm1 < reread) {
            return false;
        }
        let want_dm2 = t.had_dm2 || t.dm3 == ClearAnswer::Ack;
        if want_dm2
            && m.is_none_or(|m| m.last_dm2 < reread)
            && mon.ack_from(t.sa, DM2, reread).is_none()
        {
            return false;
        }
    }
    !(had_def && mon.last_def < reread)
}

impl CodeClearRun {
    pub fn new(kind: ClearKind) -> Self {
        Self {
            kind,
            targets: Vec::new(),
            engine_sa: 0,
            started: 0,
            reread: i64::MAX,
            sent: 0,
            rejected: 0,
            reject_why: String::new(),
            aborted: false,
            abort_why: String::new(),
            had_def: false,
            def_fresh: false,
            def_sa: -1,
            aft_before: AftState::default(),
            aft_after: AftState::default(),
            headline: String::new(),
            sa_labels: Vec::new(),
        }
    }

    pub fn kind(&self) -> ClearKind {
        self.kind
    }
    pub fn targets(&self) -> &[ClearTarget] {
        &self.targets
    }
    pub fn aborted(&self) -> bool {
        self.aborted
    }
    pub fn sent(&self) -> usize {
        self.sent
    }
    pub fn rejected(&self) -> usize {
        self.rejected
    }
    pub fn headline(&self) -> &str {
        &self.headline
    }

    pub fn target(&self, sa: i32) -> Option<&ClearTarget> {
        self.targets.iter().find(|t| t.sa == sa)
    }

    /// Run the whole clear and return the report text.
    pub fn execute(&mut self, mon: &mut BusMonitor, link: &mut dyn ClearLink) -> String {
        self.started = link.now();
        self.before(mon);
        if self.kind == ClearKind::All {
            self.phase(DM11, mon, link);
        }
        if !self.stopped() {
            self.phase(DM3, mon, link);
        }
        if !self.stopped() {
            self.read_back(mon, link);
        }
        self.after(mon, link.now());
        self.report()
    }

    fn stopped(&self) -> bool {
        self.aborted || (self.sent == 0 && self.rejected > 0)
    }

    fn before(&mut self, mon: &BusMonitor) {
        self.engine_sa = mon.engine_sa();
        let dests = destinations(self.engine_sa, &mon.diagnostic_modules());
        let recent = self.started - ON_BUS_MS;
        for sa in directed(&dests) {
            let sa = i32::from(sa);
            let mut t = ClearTarget::new(sa, mon.seen_since(sa, recent));
            if let Some(m) = mon.peek(sa) {
                t.had_dm1 = m.has_dm1();
                t.had_dm2 = m.last_dm2 != NEVER;
                t.active_before = m.active.clone();
                t.prev_before = m.prev.clone();
            }
            self.targets.push(t);
        }
        self.had_def = mon.last_def != NEVER;
        self.aft_before = mon.aft.clone();
    }

    fn phase(&mut self, pgn: i32, mon: &mut BusMonitor, link: &mut dyn ClearLink) {
        let name = phase_name(pgn);
        let since = link.now();
        let all: Vec<usize> = (0..self.targets.len()).collect();
        if self.send_requests(pgn, &all, true, link) > 0 {
            link.progress(&format!("clearing codes — {name} sent, waiting for ACK"));
            self.wait(pgn, since, ACK_WAIT_MS, mon, link);
        }
        if self.stopped() {
            return;
        }
        // One retry: the adapter refused the frame, or the module is on the bus but stayed quiet
        // or said busy.
        let again: Vec<usize> = self
            .targets
            .iter()
            .enumerate()
            .filter(|(_, t)| {
                let a = t.answer(pgn);
                a == ClearAnswer::SendFailed
                    || (t.on_bus && matches!(a, ClearAnswer::NoAnswer | ClearAnswer::Busy))
            })
            .map(|(i, _)| i)
            .collect();
        if !again.is_empty() {
            let retry = link.now();
            if self.send_requests(pgn, &again, false, link) > 0 {
                link.progress(&format!("clearing codes — {name} retry, waiting for ACK"));
                self.wait(pgn, retry, RETRY_WAIT_MS, mon, link);
            }
        }
        for t in &mut self.targets {
            if !t.on_bus && t.answer(pgn) == ClearAnswer::NoAnswer {
                t.set_answer(pgn, ClearAnswer::NotOnBus);
            }
        }
    }

    fn send_requests(
        &mut self,
        pgn: i32,
        which: &[usize],
        global: bool,
        link: &mut dyn ClearLink,
    ) -> usize {
        let mut ok = 0;
        for &i in which {
            let sent = self.send(&request_tx(pgn, self.targets[i].sa as u8), link);
            if sent {
                ok += 1;
            }
            // A retry keeps an earlier "busy" / "no answer"; only a first send sets the state.
            let t = &mut self.targets[i];
            let was = t.answer(pgn);
            if matches!(was, ClearAnswer::NotAsked | ClearAnswer::SendFailed) {
                t.set_answer(
                    pgn,
                    if sent {
                        ClearAnswer::NoAnswer
                    } else {
                        ClearAnswer::SendFailed
                    },
                );
            }
        }
        // Global last: catches a module this tool has not heard from. It is never answered.
        if global && self.send(&request_tx(pgn, BROADCAST), link) {
            ok += 1;
        }
        ok
    }

    fn send(&mut self, tx: &J1939Tx, link: &mut dyn ClearLink) -> bool {
        let ok = match guard::check(tx, SendPolicy::default()) {
            Err(r) => {
                self.reject_why = r.0;
                false
            }
            Ok(()) => {
                let ok = link.send(tx);
                if !ok {
                    let why = link.last_error();
                    if !why.is_empty() {
                        self.reject_why = why;
                    }
                }
                ok
            }
        };
        if ok {
            self.sent += 1;
        } else {
            self.rejected += 1;
        }
        ok
    }

    fn wait(
        &mut self,
        pgn: i32,
        since: i64,
        ms: i64,
        mon: &mut BusMonitor,
        link: &mut dyn ClearLink,
    ) {
        let targets = &self.targets;
        let done = |m: &BusMonitor| all_answered(targets, pgn, since, m);
        let live = link.pump(mon, ms, Some(&done));
        self.take_answers(pgn, since, mon);
        if !live {
            self.abort();
        }
    }

    fn take_answers(&mut self, pgn: i32, since: i64, mon: &BusMonitor) {
        for t in &mut self.targets {
            let a = t.answer(pgn);
            if matches!(a, ClearAnswer::NotAsked | ClearAnswer::SendFailed) {
                continue;
            }
            if let Some(r) = mon.ack_from(t.sa, pgn, since) {
                t.set_answer(pgn, from_control(r.control));
            }
        }
    }

    fn read_back(&mut self, mon: &mut BusMonitor, link: &mut dyn ClearLink) {
        link.progress("clearing codes — letting the modules re-check");
        if !link.pump(mon, SETTLE_MS, None) {
            self.abort();
            return;
        }
        self.reread = link.now();
        let mut dests: Vec<u8> = self.targets.iter().map(|t| t.sa as u8).collect();
        dests.push(BROADCAST);
        for tx in refresh_dm_frames(&dests) {
            self.send(&tx, link);
        }
        let mut def_dests = vec![unicast_dest(self.engine_sa, ENGINE_SA)];
        let def_sa = mon.def_sa;
        if (0..=253).contains(&def_sa) && !def_dests.contains(&(def_sa as u8)) {
            def_dests.push(def_sa as u8);
        }
        def_dests.push(BROADCAST);
        for tx in aftertreatment_read_frames(&def_dests) {
            self.send(&tx, link);
        }
        link.progress("clearing codes — reading DM1 / DM2 / DEF back");
        let targets = &self.targets;
        let (reread, had_def) = (self.reread, self.had_def);
        let done = |m: &BusMonitor| read_back_done(targets, reread, had_def, m);
        if !link.pump(mon, REREAD_MS, Some(&done)) {
            self.abort();
        }
    }

    fn after(&mut self, mon: &mut BusMonitor, now: i64) {
        let read_back = self.reread != i64::MAX;
        let reread = self.reread;
        let aborted = self.aborted;
        for t in &mut self.targets {
            let Some(m) = mon.peek(t.sa) else { continue };
            let (fresh_dm1, fresh_dm2) = (m.last_dm1 >= reread, m.last_dm2 >= reread);
            let (has_active, has_prev) = (!m.active.is_empty(), !m.prev.is_empty());
            let (active, prev) = (m.active.clone(), m.prev.clone());
            let lamps = (m.red, m.amber, m.protect, m.mil);
            if read_back && fresh_dm1 {
                t.fresh_dm1 = true;
                t.active_after = Some(active);
                (t.red, t.amber, t.protect, t.mil) = lamps;
            } else if read_back && !aborted && t.dm11 == ClearAnswer::Ack && has_active {
                mon.mark_cleared(t.sa, true, false, now);
                t.emptied_active = true;
                t.active_after = Some(Vec::new());
            }
            if read_back && fresh_dm2 {
                t.prev_after = Some(prev);
            } else if read_back && !aborted && t.dm3 == ClearAnswer::Ack && has_prev {
                mon.mark_cleared(t.sa, false, true, now);
                t.emptied_prev = true;
                t.prev_after = Some(Vec::new());
            }
        }
        self.def_sa = mon.def_sa;
        self.def_fresh = read_back && mon.last_def >= reread;
        self.aft_after = mon.aft.clone();
        let mut sas: Vec<i32> = self.targets.iter().map(|t| t.sa).collect();
        if self.def_sa >= 0 {
            sas.push(self.def_sa);
        }
        self.sa_labels = sas
            .into_iter()
            .map(|sa| (sa, mon.names.sa_label(sa)))
            .collect();
    }

    fn abort(&mut self) {
        if self.aborted {
            return;
        }
        self.aborted = true;
        self.abort_why = "the adapter dropped or Disconnect was pressed".to_string();
    }

    // ---------- report ----------

    fn label(&self, sa: i32) -> String {
        self.sa_labels
            .iter()
            .find(|(s, _)| *s == sa)
            .map(|(_, l)| l.clone())
            .unwrap_or_else(|| format!("SA {sa}"))
    }

    fn silent(&self, t: &ClearTarget) -> bool {
        let asked11 = self.kind == ClearKind::All;
        let quiet11 = !asked11 || matches!(t.dm11, ClearAnswer::NotOnBus | ClearAnswer::NoAnswer);
        let quiet3 = matches!(
            t.dm3,
            ClearAnswer::NotOnBus | ClearAnswer::NoAnswer | ClearAnswer::NotAsked
        );
        !t.on_bus
            && quiet11
            && quiet3
            && t.active_before.is_empty()
            && t.prev_before.is_empty()
            && !t.had_dm1
            && !t.had_dm2
            && t.active_after.is_none()
            && t.prev_after.is_none()
    }

    /// The answer that decides this kind of clear: DM11 for reset all, DM3 for clear previous.
    fn main_answer(&self, t: &ClearTarget) -> ClearAnswer {
        if self.kind == ClearKind::All {
            t.dm11
        } else {
            t.dm3
        }
    }

    /// The list this kind of clear empties, as read back after it (None = not re-read).
    fn main_after<'a>(&self, t: &'a ClearTarget) -> Option<&'a Vec<Dtc>> {
        if self.kind == ClearKind::All {
            if t.fresh_dm1 {
                t.active_after.as_ref()
            } else {
                None
            }
        } else if t.emptied_prev {
            None
        } else {
            t.prev_after.as_ref()
        }
    }

    fn main_before<'a>(&self, t: &'a ClearTarget) -> &'a Vec<Dtc> {
        if self.kind == ClearKind::All {
            &t.active_before
        } else {
            &t.prev_before
        }
    }

    /// Codes a module ACKed away and then reported again: still true, not a failed clear.
    fn came_back(&self) -> usize {
        self.targets
            .iter()
            .filter(|t| self.main_answer(t) == ClearAnswer::Ack)
            .filter_map(|t| self.main_after(t))
            .map(Vec::len)
            .sum()
    }

    /// On the bus, never answered, and still showing (or not re-sending) its codes.
    fn stuck(&self) -> Vec<&ClearTarget> {
        self.targets
            .iter()
            .filter(|t| {
                let a = self.main_answer(t);
                if !t.on_bus || !matches!(a, ClearAnswer::NoAnswer | ClearAnswer::Busy) {
                    return false;
                }
                match self.main_after(t) {
                    Some(after) => !after.is_empty(),
                    None => !self.main_before(t).is_empty(),
                }
            })
            .collect()
    }

    fn aftertreatment_after(&self) -> Vec<&Dtc> {
        self.targets
            .iter()
            .filter(|t| t.fresh_dm1)
            .filter_map(|t| t.active_after.as_ref())
            .flatten()
            .filter(|d| is_aftertreatment_spn(d.spn))
            .collect()
    }

    fn build_headline(&self) -> String {
        if self.aborted {
            return format!(
                "RESULT: INTERRUPTED — {}. Connect again and re-run the clear.",
                self.abort_why
            );
        }
        if self.sent == 0 && self.rejected > 0 {
            let why = if self.reject_why.is_empty() {
                String::new()
            } else {
                format!(" ({})", self.reject_why)
            };
            return format!(
                "RESULT: FAILED — the adapter rejected every request{why}. Nothing was sent."
            );
        }
        let mut refused_by = Vec::new();
        let mut acked = false;
        for t in &self.targets {
            if refused(t.dm11) || refused(t.dm3) {
                refused_by.push(format!("SA {}", t.sa));
            }
            if t.dm11 == ClearAnswer::Ack || t.dm3 == ClearAnswer::Ack {
                acked = true;
            }
        }
        let back = self.came_back();
        let stuck = self.stuck();
        let stuck_note = if stuck.is_empty() {
            String::new()
        } else {
            format!(
                "; {} did not answer and still show{} codes",
                stuck
                    .iter()
                    .map(|t| format!("SA {}", t.sa))
                    .collect::<Vec<_>>()
                    .join(", "),
                if stuck.len() == 1 { "s" } else { "" }
            )
        };
        let what = if self.kind == ClearKind::All {
            "active"
        } else {
            "previously active"
        };
        if !refused_by.is_empty() {
            return format!(
                "RESULT: REFUSED by {}{}. See below.",
                refused_by.join(", "),
                if acked { " (others cleared)" } else { "" }
            );
        }
        if acked && back > 0 {
            return format!(
                "RESULT: cleared — {back} {what} code{} came straight back (still true right now){stuck_note}.",
                if back == 1 { "" } else { "s" }
            );
        }
        if acked && !stuck.is_empty() {
            return format!("RESULT: partly cleared{stuck_note}.");
        }
        if acked {
            return if self.kind == ClearKind::All {
                "RESULT: cleared — no active codes came back.".to_string()
            } else {
                "RESULT: previously active codes cleared.".to_string()
            };
        }
        let (mut before, mut after, mut fresh) = (0, 0, false);
        for t in &self.targets {
            if self.kind == ClearKind::All {
                before += t.active_before.len() + t.prev_before.len();
                if t.fresh_dm1 {
                    fresh = true;
                    after += t.active_after.as_ref().map_or(0, Vec::len);
                }
                if let Some(p) = &t.prev_after {
                    after += p.len();
                }
            } else {
                before += t.prev_before.len();
                if let Some(p) = &t.prev_after {
                    fresh = true;
                    after += p.len();
                }
            }
        }
        if fresh && before > 0 && after == 0 {
            return "RESULT: codes gone — no module sent an ACK, but the fresh DM1/DM2 are empty."
                .to_string();
        }
        format!(
            "RESULT: NOT CONFIRMED — no module acknowledged the clear{}",
            if fresh && after >= before && before > 0 {
                " and the codes did not change."
            } else {
                "."
            }
        )
    }

    pub fn report(&mut self) -> String {
        self.headline = self.build_headline();
        let mut out: Vec<String> = Vec::new();
        out.push(if self.kind == ClearKind::All {
            "CODE CLEAR — reset all codes (J1939 DM11 clear active + DM3 clear previous)".into()
        } else {
            "CODE CLEAR — clear previously active codes (J1939 DM3)".into()
        });
        out.push(self.headline.clone());
        out.push(String::new());
        let asked: Vec<String> = self
            .targets
            .iter()
            .map(|t| format!("SA {}", t.sa))
            .collect();
        out.push(format!(
            "Asked directly: {}, then everyone (SA 255).",
            asked.join(", ")
        ));
        if self.rejected > 0 && self.sent > 0 {
            out.push(format!(
                "Adapter rejected {} of {} frames{}",
                self.rejected,
                self.sent + self.rejected,
                if self.reject_why.is_empty() {
                    ".".to_string()
                } else {
                    format!(": {}", self.reject_why)
                }
            ));
        }
        out.push(String::new());
        for t in &self.targets {
            self.append_target(&mut out, t);
        }
        self.append_lamps(&mut out);
        self.append_def(&mut out);
        self.append_advice(&mut out);
        let text = out.join("\r\n");
        format!("{}\r\n", text.trim_end())
    }

    fn append_target(&self, out: &mut Vec<String>, t: &ClearTarget) {
        let label = self.label(t.sa);
        if self.silent(t) {
            out.push(format!(
                "{label} — not on the bus (nothing heard from this address); request sent anyway."
            ));
            return;
        }
        out.push(label);
        if self.kind == ClearKind::All {
            out.push(format!(
                "  DM11 clear active: {}    DM3 clear previous: {}",
                answer_text(t.dm11),
                answer_text(t.dm3)
            ));
        } else {
            out.push(format!("  DM3 clear previous: {}", answer_text(t.dm3)));
        }
        if self.kind == ClearKind::All {
            if t.emptied_active {
                out.push(format!("  Active: {} before → 0 (ACKed, no DM1 since; the list was emptied — a new DM1 puts back anything still active)", t.active_before.len()));
            } else if let Some(after) = &t.active_after {
                if !t.active_before.is_empty() || !after.is_empty() {
                    out.push(format!(
                        "  Active: {} before → {} now",
                        t.active_before.len(),
                        after.len()
                    ));
                }
                append_codes(out, "    still active: ", after);
            } else if !t.active_before.is_empty() {
                out.push(format!(
                    "  Active: {} before → no DM1 since the clear (the list still shows what it last sent)",
                    t.active_before.len()
                ));
            }
        }
        if t.emptied_prev {
            out.push(format!(
                "  Previous: {} before → 0 (ACKed, no DM2 since; the list was emptied)",
                t.prev_before.len()
            ));
        } else if let Some(after) = &t.prev_after {
            if !t.prev_before.is_empty() || !after.is_empty() {
                out.push(format!(
                    "  Previous: {} before → {} now",
                    t.prev_before.len(),
                    after.len()
                ));
            }
            append_codes(out, "    still previously active: ", after);
        } else if !t.prev_before.is_empty() {
            out.push(format!(
                "  Previous: {} before → no DM2 since the clear",
                t.prev_before.len()
            ));
        }
    }

    fn append_lamps(&self, out: &mut Vec<String>) {
        if self.kind != ClearKind::All {
            return;
        }
        let eng = self.target(i32::from(unicast_dest(self.engine_sa, ENGINE_SA)));
        out.push(String::new());
        match eng {
            Some(e) if e.fresh_dm1 => out.push(format!(
                "Engine lamps now: Red Stop {}   Amber {}{}{}  (fresh DM1, SA {}). Lamps are DM1 bits; they follow the active codes.",
                if e.red { "ON" } else { "off" },
                if e.amber { "ON" } else { "off" },
                if e.protect { "   Protect ON" } else { "" },
                if e.mil { "   MIL ON" } else { "" },
                e.sa
            )),
            _ => out.push("Engine lamps: no DM1 from the engine since the clear.".to_string()),
        }
    }

    fn append_def(&self, out: &mut Vec<String>) {
        out.push(String::new());
        if !self.had_def && !self.def_fresh {
            out.push("DEF / SCR: no DEF tank message (PGN FE56) on this hookup — the tank header or the module that reports it is not talking.".to_string());
            return;
        }
        let from = if self.def_sa >= 0 {
            format!(" from {}", self.label(self.def_sa))
        } else {
            String::new()
        };
        out.push(if self.def_fresh {
            format!("DEF / SCR (PGN FE56{from}, read after the clear):")
        } else {
            format!("DEF / SCR (PGN FE56{from}, NOT re-sent since the clear — last values):")
        });
        for line in self
            .aft_after
            .text()
            .split("\r\n")
            .filter(|l| !l.is_empty())
        {
            out.push(format!("  {line}"));
        }
        if self.aft_before.severity_raw >= 0
            && self.aft_before.severity_raw != self.aft_after.severity_raw
        {
            out.push(format!(
                "  SCR inducement was: {}",
                self.aft_before.severity
            ));
        }
    }

    fn append_advice(&self, out: &mut Vec<String>) {
        let mut tips: Vec<String> = Vec::new();
        if self.aborted {
            tips.push("The clear was interrupted before it could be confirmed. Connect again and re-run it.".into());
        }
        if self.sent == 0 && self.rejected > 0 {
            tips.push("The adapter refused every frame. Disconnect, close USB-Link Explorer / Guidanz, Connect again, then retry. If it keeps failing, check the Log for the tool address claim.".into());
        }
        let mut refused_by = Vec::new();
        let mut quiet = Vec::new();
        let mut quiet_compressor = false;
        for t in &self.targets {
            if refused(t.dm11) || refused(t.dm3) {
                refused_by.push(self.label(t.sa));
            } else if !self.aborted
                && t.on_bus
                && (t.dm11 == ClearAnswer::NoAnswer || t.dm3 == ClearAnswer::NoAnswer)
            {
                quiet.push(self.label(t.sa));
                if t.sa == i32::from(COMPRESSOR_SA) {
                    quiet_compressor = true;
                }
            }
        }
        if !refused_by.is_empty() {
            tips.push(format!("{} refused the clear. Many ECMs only clear with key ON, engine OFF — shut the engine down, leave the key ON, and clear again.", refused_by.join(", ")));
        }
        if !quiet.is_empty() {
            tips.push(format!(
                "{} did not answer, even after a retry. It may not take J1939 clears from a service tool{}",
                quiet.join(", "),
                if quiet_compressor {
                    "; some compressor controllers only reset faults from their own keypad or display."
                } else {
                    "."
                }
            ));
        }
        if self.came_back() > 0 {
            tips.push("Codes that came straight back are still true right now. The clear worked; the fault did not go away. Fix it, then clear again.".into());
        }
        let aft = self.aftertreatment_after();
        let tank_dead = aft
            .iter()
            .any(|d| matches!(d.spn, 1761 | 3031 | 3364) && d.fmi == 9);
        let sev = self.aft_after.severity_raw;
        if tank_dead {
            tips.push("DEF tank header is not talking (FMI 9 on 1761 / 3031 / 3364). Nothing DEF-related will stay cleared until it talks — check its power, ground, and CAN at the tank.".into());
        }
        if !aft.is_empty() || (1..=5).contains(&sev) {
            tips.push("DEF/SCR: a code clear does not end SCR inducement (SPN 5246) by itself. The ECM drops it once it sees the repair working: DEF level, temp, and quality reading real values (above), then key OFF until the ECM powers down, key ON, and run the engine while you watch SCR inducement go to \"not active\". Guidanz aftertreatment reset may still be required. Tech Bench never disables DEF/SCR.".into());
        }
        if tips.is_empty() {
            return;
        }
        out.push(String::new());
        out.push("What to do:".to_string());
        for tip in tips {
            out.push(format!("- {tip}"));
        }
    }
}

fn append_codes(out: &mut Vec<String>, prefix: &str, codes: &[Dtc]) {
    for d in codes {
        let name = if d.name.is_empty() {
            String::new()
        } else {
            format!("  {}", d.name)
        };
        let tag = if is_aftertreatment_spn(d.spn) {
            "  [DEF/SCR]"
        } else {
            ""
        };
        out.push(format!("{prefix}SPN {} FMI {}{name}{tag}", d.spn, d.fmi));
    }
}

/// An Acknowledgment frame as a module sends it, for scripted links and tests.
pub fn ack_frame(sa: i32, control: i32, pgn: i32, da: i32, address: i32) -> J1939Frame {
    J1939Frame {
        pgn: ACK,
        sa,
        da,
        data: vec![
            control as u8,
            0xFF,
            0xFF,
            0xFF,
            address as u8,
            (pgn & 0xFF) as u8,
            ((pgn >> 8) & 0xFF) as u8,
            ((pgn >> 16) & 0xFF) as u8,
        ],
    }
}
