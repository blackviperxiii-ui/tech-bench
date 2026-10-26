//! The web build's core: tb-core compiled to wasm32 behind one call, `{"cmd", "args"}` in and
//! `{"ok": true, "data"}` or `{"ok": false, "error"}` out, the same shape as the RP1210 bridge.
//! Plain exports, no wasm-bindgen, so the build needs only `cargo` and the wasm32 target.
//!
//! The browser keeps the work orders (IndexedDB) and passes them in; this side does what the
//! desktop store does to them (key, label, order, number check, clock). The KB is the shipped
//! JSON baked into the module; the password DB, shop notes and shop files are not part of it.
//! There is no bus here: the adapter needs the Windows build.
//!
//! wasm32 has no clock, so writes take the browser's UTC time in `now`.

use std::cell::RefCell;

use tb_core::json::{self, Value};
use tb_core::kb::{KbIndex, MAX_RESULTS};
use tb_core::search::{codebook, featurebook};
use tb_core::workorders::job_strip;
use tb_core::workorders::store::safe_key;
use tb_core::workorders::{WorkOrder, WorkOrderClock};

const KB: &str = include_str!("../../../kb/data/kb.json");
const MANUALS: &str = include_str!("../../../kb/data/usb-manuals.json");
const FILTERS: &str = include_str!("../../../kb/data/rental-portable-filters-oil.json");
const EQUIP: &str = include_str!("../../../kb/data/rental-equipment-info.json");
const CHEATSHEET_RTF: &str = include_str!("../../../assets/cheatsheet.rtf");

/// What the KB status line calls this build's KB folder.
pub const KB_ROOT: &str = "(built into the web app)";

thread_local! {
    static INDEX: RefCell<Option<KbIndex>> = const { RefCell::new(None) };
    static OUT: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) };
}

fn with_kb<T>(f: impl FnOnce(&KbIndex) -> T) -> T {
    INDEX.with(|cell| {
        let mut slot = cell.borrow_mut();
        let kb = slot.get_or_insert_with(|| {
            let mut kb = KbIndex::new();
            kb.load_bundled(
                KB_ROOT,
                &[
                    ("kb.json", KB),
                    ("usb-manuals.json", MANUALS),
                    ("rental-portable-filters-oil.json", FILTERS),
                    ("rental-equipment-info.json", EQUIP),
                ],
            );
            kb
        });
        f(kb)
    })
}

fn st(v: &str) -> Value {
    Value::String(v.to_string())
}

fn obj(members: Vec<(&str, Value)>) -> Value {
    Value::Object(
        members
            .into_iter()
            .map(|(k, v)| (k.to_string(), v))
            .collect(),
    )
}

fn strings<S: AsRef<str>>(items: &[S]) -> Value {
    Value::Array(items.iter().map(|s| st(s.as_ref())).collect())
}

fn num(n: usize) -> Value {
    Value::Number(n.to_string())
}

/// A whole-number argument; JSON numbers keep their source spelling here.
fn int(args: &Value, key: &str) -> Result<i32, String> {
    json::s(args, key)
        .parse()
        .map_err(|_| format!("{key} must be a whole number"))
}

/// A work order as the UI's `WorkOrder` (camelCase, plus tb-core's key, label and clock lines).
pub fn wo_dto(w: &WorkOrder) -> Value {
    obj(vec![
        ("number", st(&w.number)),
        ("segment", st(&w.segment)),
        ("customer", st(&w.customer)),
        ("customerNo", st(&w.customer_no)),
        ("model", st(&w.model)),
        ("serial", st(&w.serial)),
        ("stock", st(&w.stock)),
        ("description", st(&w.description)),
        ("assignedTech", st(&w.assigned_tech)),
        ("notes", st(&w.notes)),
        ("reportText", st(&w.report_text)),
        ("source", st(&w.source)),
        ("clockState", st(&w.clock_state)),
        ("updatedUtc", st(&w.updated_utc)),
        ("folder", st(&w.folder)),
        ("media", strings(&w.media)),
        (
            "clock",
            Value::Array(
                w.clock
                    .iter()
                    .map(|c| {
                        obj(vec![
                            ("kind", st(&c.kind)),
                            ("whenUtc", st(&c.when_utc)),
                            ("detail", st(&c.detail)),
                        ])
                    })
                    .collect(),
            ),
        ),
        ("key", st(&w.key())),
        ("label", st(&w.list_label())),
        (
            "clockLines",
            Value::Array(w.clock.iter().map(|c| st(&c.to_string())).collect()),
        ),
    ])
}

/// The UI's `WorkOrder` back to tb-core's (key, label and clock lines are recomputed).
pub fn wo_from_dto(d: &Value) -> Result<WorkOrder, String> {
    if !d.is_object() {
        return Err("No work order sent.".into());
    }
    let s = |k: &str| json::s(d, k);
    Ok(WorkOrder {
        number: s("number"),
        segment: s("segment"),
        customer: s("customer"),
        customer_no: s("customerNo"),
        model: s("model"),
        serial: s("serial"),
        stock: s("stock"),
        description: s("description"),
        assigned_tech: s("assignedTech"),
        notes: s("notes"),
        report_text: s("reportText"),
        source: s("source"),
        clock_state: s("clockState"),
        updated_utc: s("updatedUtc"),
        folder: s("folder"),
        media: d
            .get("media")
            .map(|m| m.items().iter().map(Value::text).collect())
            .unwrap_or_default(),
        clock: d
            .get("clock")
            .map(|c| {
                c.items()
                    .iter()
                    .map(|e| WorkOrderClock {
                        kind: json::s(e, "kind"),
                        when_utc: json::s(e, "whenUtc"),
                        detail: json::s(e, "detail"),
                    })
                    .collect()
            })
            .unwrap_or_default(),
    })
}

/// `WorkOrderStore.save` without the disk: the number check and the `UpdatedUtc` stamp. Returns
/// the saved order, its storage id (the packet folder name) and the `wo.json` text to keep.
fn save(mut w: WorkOrder, now: &str) -> Result<Value, String> {
    if w.number.trim().is_empty() {
        return Err("Work order number is required.".into());
    }
    w.updated_utc = now.to_string();
    Ok(obj(vec![
        ("id", st(&safe_key(&w.key()))),
        ("packet", st(&w.to_json())),
        ("wo", wo_dto(&w)),
    ]))
}

fn now_arg(args: &Value) -> Result<String, String> {
    let now = json::s(args, "now");
    if now.trim().is_empty() {
        return Err("The browser sent no time.".into());
    }
    Ok(now)
}

/// The list the desktop's `wo_list` returns, from the `wo.json` texts the browser keeps.
fn list(packets: &[Value]) -> Value {
    let mut orders: Vec<WorkOrder> = packets
        .iter()
        .filter_map(|p| WorkOrder::from_json(&p.text()))
        .filter(|w| !w.key().trim().is_empty())
        .map(|mut w| {
            if w.source.trim().is_empty() {
                w.source = "file".into();
            }
            w
        })
        .collect();
    // Same order as the desktop list: number, then segment, ignoring case.
    orders.sort_by(|a, b| {
        a.number
            .to_uppercase()
            .cmp(&b.number.to_uppercase())
            .then_with(|| a.segment.to_uppercase().cmp(&b.segment.to_uppercase()))
    });
    obj(vec![
        ("orders", Value::Array(orders.iter().map(wo_dto).collect())),
        ("conflicts", Value::Array(Vec::new())),
        ("techId", st("")),
        ("folder", st("")),
        ("pickRows", strings(&job_strip::pick_items(&orders, "").0)),
        ("syncChip", st(job_strip::SYNC_IDLE)),
    ])
}

fn run(cmd: &str, args: &Value) -> Result<Value, String> {
    match cmd {
        "kb_info" => Ok(with_kb(|kb| {
            obj(vec![
                ("root", st(&kb.root)),
                ("status", st(&kb.status)),
                ("count", num(kb.all.len())),
                ("errors", strings(&kb.errors)),
            ])
        })),
        "kb_search" => Ok(with_kb(|kb| {
            let (hits, total) =
                kb.search(&json::s(args, "query"), &json::s(args, "kind"), MAX_RESULTS);
            obj(vec![
                (
                    "hits",
                    Value::Array(
                        hits.into_iter()
                            .map(|h| {
                                obj(vec![
                                    ("kind", st(&h.kind)),
                                    ("title", st(&h.title)),
                                    ("subtitle", st(&h.subtitle)),
                                    ("body", st(&h.body)),
                                    ("path", st(&h.path)),
                                ])
                            })
                            .collect(),
                    ),
                ),
                ("total", num(total)),
                ("status", st(&kb.status)),
            ])
        })),
        "cheatsheet" => Ok(Value::Array(
            tb_core::rtf::paragraphs(CHEATSHEET_RTF)
                .into_iter()
                .map(|p| {
                    obj(vec![
                        ("text", st(&p.text)),
                        ("bold", Value::Bool(p.bold)),
                        ("big", Value::Bool(p.big)),
                    ])
                })
                .collect(),
        )),
        "explain_code" => {
            let (spn, fmi) = (int(args, "spn")?, int(args, "fmi")?);
            Ok(with_kb(|kb| {
                obj(vec![
                    ("text", st(&codebook::explain(spn, fmi, &kb.names))),
                    ("kb", kb.spn_text(spn, fmi).map_or(Value::Null, |t| st(&t))),
                ])
            }))
        }
        "guidanz_features" => Ok(Value::Array(
            featurebook::guidanz_features()
                .into_iter()
                .map(|r| {
                    obj(vec![
                        ("id", st(&r.id)),
                        ("name", st(&r.name)),
                        ("state", st(&r.state)),
                        ("short", st(&r.short)),
                        ("detail", st(&r.detail)),
                    ])
                })
                .collect(),
        )),
        "wo_list" => Ok(list(args.get("packets").map(Value::items).unwrap_or(&[]))),
        "wo_save" => save(
            wo_from_dto(args.get("wo").unwrap_or(&Value::Null))?,
            &now_arg(args)?,
        ),
        "wo_clock" => {
            let kind = json::s(args, "kind");
            let state = match kind.as_str() {
                "logon" => "logged-on",
                "logoff" => "logged-off",
                _ => return Err(format!("unknown clock kind {kind}")),
            };
            let now = now_arg(args)?;
            let mut w = wo_from_dto(args.get("wo").unwrap_or(&Value::Null))?;
            w.clock_state = state.into();
            w.clock.push(WorkOrderClock {
                kind: format!("shop-{kind}"),
                when_utc: now.clone(),
                detail: "Shop record only.".into(),
            });
            save(w, &now)
        }
        "job_title" => {
            let wo = match args.get("wo") {
                Some(v) if v.is_object() => Some(wo_from_dto(v)?),
                _ => None,
            };
            let tag = job_strip::job_tag(
                wo.as_ref(),
                &json::s(args, "model"),
                &json::s(args, "serial"),
            );
            Ok(obj(vec![
                (
                    "title",
                    st(&job_strip::window_title(&json::s(args, "version"), &tag)),
                ),
                ("tag", st(&tag)),
            ]))
        }
        _ => Err(format!("Not in the web build: {cmd}")),
    }
}

/// One request as JSON text; the reply as JSON text.
pub fn handle(request: &str) -> String {
    let reply = match json::parse(request) {
        Ok(req) => run(
            &json::s(&req, "cmd"),
            req.get("args").unwrap_or(&Value::Null),
        ),
        Err(e) => Err(e.to_string()),
    };
    match reply {
        Ok(data) => obj(vec![("ok", Value::Bool(true)), ("data", data)]),
        Err(e) => obj(vec![("ok", Value::Bool(false)), ("error", st(&e))]),
    }
    .to_json()
}

/// Room for a request of `len` bytes; the caller writes it there and passes it to `tb_call`.
#[no_mangle]
pub extern "C" fn tb_alloc(len: usize) -> *mut u8 {
    let mut buf = Vec::<u8>::with_capacity(len);
    let ptr = buf.as_mut_ptr();
    std::mem::forget(buf);
    ptr
}

/// Runs the UTF-8 request at `ptr` (from `tb_alloc(len)`, which this frees) and returns the
/// reply's length; the reply stays at `tb_out()` until the next call.
///
/// # Safety
/// `ptr` must come from `tb_alloc(len)` with `len` bytes written, and is not used again.
#[no_mangle]
pub unsafe extern "C" fn tb_call(ptr: *mut u8, len: usize) -> usize {
    let bytes = Vec::from_raw_parts(ptr, len, len);
    let reply = handle(&String::from_utf8_lossy(&bytes));
    OUT.with(|o| {
        let mut o = o.borrow_mut();
        *o = reply.into_bytes();
        o.len()
    })
}

/// Where the last reply is.
#[no_mangle]
pub extern "C" fn tb_out() -> *const u8 {
    OUT.with(|o| o.borrow().as_ptr())
}
