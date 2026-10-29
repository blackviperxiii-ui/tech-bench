//! The web build's one call, natively: the same JSON the browser sends and gets back.

use tb_core::json::{self, Value};
use tb_wasm::{handle, tb_alloc, tb_call, tb_out};

const NOW: &str = "2026-10-26T12:00:00.0000000Z";

fn call(req: &str) -> Value {
    json::parse(&handle(req)).expect("reply is JSON")
}

fn data(req: &str) -> Value {
    let r = call(req);
    assert_eq!(r.get("ok"), Some(&Value::Bool(true)), "{}", r.to_json());
    r.get("data").cloned().unwrap()
}

fn error(req: &str) -> String {
    let r = call(req);
    assert_eq!(r.get("ok"), Some(&Value::Bool(false)), "{}", r.to_json());
    json::s(&r, "error")
}

fn blank(number: &str, segment: &str) -> String {
    format!(
        r#"{{"number":"{number}","segment":"{segment}","customer":" Acme Rental ","customerNo":"","model":"HP450","serial":"12345","stock":"","description":"No start","assignedTech":"","notes":"line 1\nline 2","reportText":"","source":"","clockState":"idle","updatedUtc":"","folder":"","media":[],"clock":[],"key":"","label":"(new)","clockLines":[]}}"#
    )
}

#[test]
fn kb_is_the_shipped_json_without_passwords() {
    let d = data(r#"{"cmd":"kb_info"}"#);
    assert_eq!(json::s(&d, "root"), "(built into the web app)");
    assert_eq!(
        json::s(&d, "status"),
        "Codes 155  ·  Passwords 0  ·  Manuals 2147  ·  Filters 63  ·  Equipment 37"
    );
    assert_eq!(d.get("errors").unwrap().items().len(), 0);

    let r = data(r#"{"cmd":"kb_search","args":{"query":"1AVPT","kind":"CODE"}}"#);
    assert_eq!(json::s(&r, "total"), "1");
    let hit = &r.get("hits").unwrap().items()[0];
    assert_eq!(json::s(hit, "kind"), "CODE");
    assert_eq!(
        json::s(hit, "title"),
        "ingersoll-rand  01  —  Sensor Failure 1AVPT"
    );

    let p = data(r#"{"cmd":"kb_search","args":{"query":"","kind":"PASSWORD"}}"#);
    assert_eq!(json::s(&p, "total"), "0");
}

#[test]
fn save_needs_a_number_and_stamps_the_browser_time() {
    assert_eq!(
        error(&format!(
            r#"{{"cmd":"wo_save","args":{{"wo":{},"now":"{NOW}"}}}}"#,
            blank("  ", "")
        )),
        "Work order number is required."
    );
    assert_eq!(
        error(&format!(
            r#"{{"cmd":"wo_save","args":{{"wo":{}}}}}"#,
            blank("7", "")
        )),
        "The browser sent no time."
    );

    let saved = data(&format!(
        r#"{{"cmd":"wo_save","args":{{"wo":{},"now":"{NOW}"}}}}"#,
        blank("WO 7/01", "2")
    ));
    assert_eq!(json::s(&saved, "id"), "WO-7-01-2");
    let wo = saved.get("wo").unwrap();
    assert_eq!(json::s(wo, "key"), "WO 7/01-2");
    assert_eq!(json::s(wo, "updatedUtc"), NOW);
    assert_eq!(json::s(wo, "label"), "WO 7/01-2  Acme Rental  HP450 12345");
    // The packet is the desktop's wo.json, C# field order, Folder always empty.
    assert_eq!(
        json::s(&saved, "packet"),
        format!(
            r#"{{"Number":"WO 7/01","Segment":"2","Customer":" Acme Rental ","CustomerNo":"","Model":"HP450","Serial":"12345","Stock":"","Description":"No start","AssignedTech":"","Notes":"line 1\nline 2","ReportText":"","Source":"","ClockState":"idle","UpdatedUtc":"{NOW}","Folder":"","Media":[],"Clock":[]}}"#
        )
    );
}

#[test]
fn clock_adds_a_shop_record() {
    let saved = data(&format!(
        r#"{{"cmd":"wo_clock","args":{{"wo":{},"kind":"logon","now":"{NOW}"}}}}"#,
        blank("88", "")
    ));
    let wo = saved.get("wo").unwrap();
    assert_eq!(json::s(wo, "clockState"), "logged-on");
    assert_eq!(
        wo.get("clockLines").unwrap().items(),
        [Value::String(format!(
            "{NOW}  shop-logon  [shop]  Shop record only."
        ))]
    );
    assert_eq!(
        json::s(wo, "label"),
        "88  Acme Rental  HP450 12345  [logged on]"
    );
    assert_eq!(
        error(&format!(
            r#"{{"cmd":"wo_clock","args":{{"wo":{},"kind":"lunch","now":"{NOW}"}}}}"#,
            blank("88", "")
        )),
        "unknown clock kind lunch"
    );
}

#[test]
fn list_reopens_saved_packets_in_desktop_order() {
    let packet = |n: &str, seg: &str| {
        let saved = data(&format!(
            r#"{{"cmd":"wo_save","args":{{"wo":{},"now":"{NOW}"}}}}"#,
            blank(n, seg)
        ));
        json::s(&saved, "packet")
    };
    let packets = Value::Array(vec![
        Value::String(packet("b20", "")),
        Value::String(packet("A10", "2")),
        Value::String("{ not a packet".into()),
        Value::String(packet("a10", "1")),
    ]);
    let d = data(&format!(
        r#"{{"cmd":"wo_list","args":{{"packets":{}}}}}"#,
        packets.to_json()
    ));
    let orders = d.get("orders").unwrap().items();
    let keys: Vec<String> = orders.iter().map(|o| json::s(o, "key")).collect();
    assert_eq!(keys, ["a10-1", "A10-2", "b20"]);
    assert_eq!(json::s(&orders[0], "source"), "file");
    assert_eq!(json::s(&orders[0], "notes"), "line 1\nline 2");
    assert_eq!(
        d.get("pickRows").unwrap().items(),
        [
            "(no work order)",
            "a10-1  Acme Rental  HP450 12345",
            "A10-2  Acme Rental  HP450 12345",
            "b20  Acme Rental  HP450 12345"
        ]
        .map(|s| Value::String(s.into()))
    );
    assert_eq!(json::s(&d, "syncChip"), "Sync: idle");
}

#[test]
fn job_title_and_explainer() {
    let t = data(&format!(
        r#"{{"cmd":"job_title","args":{{"wo":{},"model":"x","serial":"y","version":"0.1.0"}}}}"#,
        blank("7", "")
    ));
    assert_eq!(json::s(&t, "tag"), "WO 7  HP450 12345");
    assert_eq!(
        json::s(&t, "title"),
        "Tech Bench 0.1.0  ·  WO 7  HP450 12345"
    );
    let t = data(
        r#"{"cmd":"job_title","args":{"wo":null,"model":" HP450","serial":"","version":"0.1.0"}}"#,
    );
    assert_eq!(json::s(&t, "tag"), "HP450");

    let e = data(r#"{"cmd":"explain_code","args":{"spn":5246,"fmi":0}}"#);
    assert!(json::s(&e, "text").contains("SPN 5246"), "{}", e.to_json());
    assert_eq!(
        error(r#"{"cmd":"explain_code","args":{"spn":"x","fmi":0}}"#),
        "spn must be a whole number"
    );
    let rows = data(r#"{"cmd":"guidanz_features"}"#);
    assert_eq!(
        json::s(&rows.items()[0], "name"),
        "Engine Protection Shutdown"
    );
    assert!(!data(r#"{"cmd":"cheatsheet"}"#).items().is_empty());
}

#[test]
fn no_bus_and_bad_requests_answer_with_an_error() {
    assert_eq!(
        error(r#"{"cmd":"bus_call","args":{"cmd":"clear"}}"#),
        "Not in the web build: bus_call"
    );
    assert!(error("{ nope").starts_with("invalid JSON at byte "));
}

#[test]
fn raw_exports_round_trip() {
    let req = br#"{"cmd":"kb_info"}"#;
    let reply = unsafe {
        let p = tb_alloc(req.len());
        std::ptr::copy_nonoverlapping(req.as_ptr(), p, req.len());
        let n = tb_call(p, req.len());
        String::from_utf8(std::slice::from_raw_parts(tb_out(), n).to_vec()).unwrap()
    };
    assert!(
        reply.starts_with(r#"{"ok":true,"data":{"root":"(built into the web app)""#),
        "{reply}"
    );
}
