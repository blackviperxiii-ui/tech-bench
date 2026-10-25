//! Ported from tests/SelfTest.Search.cs (CodeBook.Explain, FeatureBook.Live, FeatureBook.GuidanzFeatures).

use tb_core::j1939::Names;
use tb_core::search::codebook::explain;
use tb_core::search::featurebook::{guidanz_features, live, SwitchRow};

const DASH: &str = "—";

fn lines(text: &str) -> Vec<String> {
    text.replace("\r\n", "\n")
        .split('\n')
        .map(str::to_string)
        .collect()
}

fn has_line(ln: &[String], want: &str) -> bool {
    // C#: Array.IndexOf(ln, want) > 0
    ln.iter().position(|l| l == want).is_some_and(|i| i > 0)
}

fn ex(spn: i32, fmi: i32) -> String {
    explain(spn, fmi, &Names::new())
}

#[test]
fn codebook_explain_layout_and_lookups() {
    let t = ex(5246, 0);
    let ln = lines(&t);
    assert_eq!(ln[0], "SCR operator inducement severity");
    assert_eq!(
        ln[1],
        format!("SPN 5246   FMI 0 {DASH} above normal (most severe)")
    );
    assert_eq!(ln[2], "");
    assert!(
        ln[3].starts_with("SCR operator inducement severity. FMI 0 = most severe"),
        "{}",
        ln[3]
    );
    assert!(has_line(&ln, "This FMI means:"));
    assert!(has_line(&ln, "Value is out of range in the most severe band. The sensor is talking; the number is too high or too low."));
    assert!(t.contains("YES. This is in the no-fuel / Red Stop path on T4F."));
    assert!(has_line(
        &ln,
        &format!(
            "- Then Guidanz aftertreatment reset {DASH} a generic clear often will not drop 5246"
        )
    ));
    assert!(t
        .trim_end()
        .ends_with("that wrecks the aftertreatment and the engine."));

    let u = ex(99999, 7);
    let ul = lines(&u);
    assert_eq!(ul[0], "SPN 99999");
    assert_eq!(ul[1], format!("SPN 99999   FMI 7 {DASH} not responding"));
    assert!(u.contains("see Cummins troubleshooting for SPN 99999'."));
    assert!(has_line(&ul, "not responding."));
    assert!(has_line(
        &ul,
        "Usually not by itself. Watch whether Red Stop / 5246 is also active."
    ));
    assert!(has_line(
        &ul,
        "- Confirm whether the SPN is n/a (not talking) vs a real out-of-range number"
    ));

    let d = ex(1761, 9);
    let dl = lines(&d);
    assert_eq!(
        dl[1],
        format!("SPN 1761   FMI 9 {DASH} abnormal update rate (not talking)")
    );
    assert!(d.contains(&format!("Abnormal update rate {DASH} the ECM is not receiving messages from that device. This is NOT a 'wrong number'.")));
    assert!(d.contains("Indirectly. These do not inject fuel themselves"));
    assert!(d.contains("- All three FMI 9 together = whole module offline, not 'low DEF'"));

    assert!(ex(190, 4).contains("Can. No crank RPM or no cam/crank sync = no inject."));
    assert!(ex(723, 2).contains("Can. No crank RPM or no cam/crank sync = no inject."));
    assert!(ex(157, 3).contains("Can, if rail/supply never builds while cranking."));
    assert!(ex(94, 3).contains("Can, if rail/supply never builds while cranking."));
    assert!(ex(110, 3).contains("Usually not by itself."));

    assert!(ex(110, 1).contains("Value is out of range in the most severe band."));
    assert!(ex(110, 3).contains("Signal voltage shorted high (usually 5 V or battery)."));
    assert!(ex(110, 4).contains("Signal voltage shorted low (ground)."));
    assert!(ex(110, 18).contains("Moderate over/under range. Sensor is alive; reading is off."));
    assert!(ex(110, 31).contains("Condition exists. Often a status/result"));

    assert!(ex(1079, 3).contains("5 V sensor supply. If this is shorted"));
    assert!(ex(1080, 3).contains("5 V sensor supply. If this is shorted"));
    assert!(ex(4334, 3).contains("DEF system pressure at the doser."));
    assert!(ex(5394, 3).contains("DEF pump command/state."));
    assert!(!ex(110, 3).contains("5 V sensor supply."));
}

#[test]
fn codebook_lines_end_in_crlf_like_appendline() {
    let t = ex(110, 3);
    assert!(t.ends_with("force a start\r\n"), "{t:?}");
    assert!(
        t.starts_with("Coolant temp\r\nSPN 110   FMI 3 — voltage high\r\n\r\n"),
        "{t:?}"
    );
}

#[test]
fn codebook_title_uses_name_overrides() {
    let mut names = Names::new();
    names.load(
        Some([(4001, "Shop-added SPN".to_string())].into_iter().collect()),
        None,
        None,
    );
    assert_eq!(lines(&explain(4001, 2, &names))[0], "Shop-added SPN");
}

fn find<'a>(rows: &'a [SwitchRow], id: &str) -> &'a SwitchRow {
    rows.iter()
        .find(|r| r.id == id)
        .unwrap_or_else(|| panic!("no row {id}"))
}

fn ids(rows: &[SwitchRow]) -> String {
    rows.iter()
        .map(|r| r.id.as_str())
        .collect::<Vec<_>>()
        .join(",")
}

#[test]
fn featurebook_live_rows() {
    let off = live(false, true, true, true, true, true, 900.0, "12.5 %");
    assert_eq!(off.len(), 1);
    assert_eq!(off[0].id, "link");
    assert_eq!(off[0].state, "OFF");
    assert_eq!(off[0].short, "Not talking to the ECM.");
    assert_eq!(
        off[0].to_string(),
        format!(
            "OFF{}INLINE connection  {DASH}  Not talking to the ECM.",
            " ".repeat(9)
        )
    );

    let quiet = live(true, false, false, false, false, false, 0.0, "");
    assert_eq!(ids(&quiet), "red,amber,induc,tank,defval,prot,run");
    assert_eq!(find(&quiet, "red").state, "off");
    assert_eq!(
        find(&quiet, "red").short,
        format!("No Red Stop {DASH} no lamp-only reset needed.")
    );
    assert_eq!(find(&quiet, "amber").state, "off");
    assert_eq!(find(&quiet, "induc").state, "off");
    assert_eq!(find(&quiet, "tank").state, "check data");
    assert_eq!(find(&quiet, "defval").state, DASH);
    assert_eq!(find(&quiet, "prot").state, "off");
    assert_eq!(find(&quiet, "run").state, "STOPPED");
    assert_eq!(find(&quiet, "run").short, "RPM 0");

    let hot = live(
        true,
        true,
        true,
        true,
        true,
        true,
        850.0,
        "n/a (not talking)",
    );
    assert_eq!(find(&hot, "red").state, "ON");
    assert_eq!(
        find(&hot, "red").short,
        "ECM is commanding no-fuel / shutdown."
    );
    assert_eq!(find(&hot, "amber").state, "ON");
    assert_eq!(
        find(&hot, "amber").short,
        "A warning / derate path is active."
    );
    assert_eq!(find(&hot, "induc").state, "LOCKED");
    assert_eq!(
        find(&hot, "induc").short,
        format!("Most-severe inducement {DASH} will not fuel.")
    );
    assert_eq!(find(&hot, "tank").state, "NOT TALKING");
    assert_eq!(
        find(&hot, "tank").short,
        format!("FMI 9 on 1761/3031/3364 {DASH} module offline.")
    );
    assert_eq!(find(&hot, "defval").state, "n/a (not talking)");
    assert_eq!(find(&hot, "prot").state, "ON");
    assert_eq!(find(&hot, "run").state, "RUNNING");
    assert_eq!(find(&hot, "run").short, "RPM 850");
    assert_eq!(
        find(&hot, "red").to_string(),
        format!(
            "ON{}Red Stop lamp  {DASH}  ECM is commanding no-fuel / shutdown.",
            " ".repeat(10)
        )
    );

    let red_only = live(true, true, false, false, false, false, 0.0, "x");
    assert_eq!(find(&red_only, "red").state, "ON");
    assert_eq!(find(&red_only, "amber").state, "off");
    assert_eq!(find(&red_only, "prot").state, "off");

    let run = |rpm: f64| {
        live(true, false, false, false, false, false, rpm, "")
            .into_iter()
            .find(|r| r.id == "run")
            .unwrap()
    };
    assert_eq!(run(-1.0).state, DASH);
    assert_eq!(run(-1.0).short, "No EEC1 yet.");
    assert_eq!(run(49.0).state, "STOPPED");
    assert_eq!(run(50.0).state, "CRANKING");
    assert_eq!(run(399.0).state, "CRANKING");
    assert_eq!(run(400.0).state, "RUNNING");
}

#[test]
fn featurebook_guidanz_list() {
    let g = guidanz_features();
    assert_eq!(g.len(), 13);
    assert_eq!(
        ids(&g),
        "ep,lop,hct,idle,warm,pto,fan,acc,max,start,def,vgt,egr"
    );
    assert_eq!(find(&g, "ep").name, "Engine Protection Shutdown");
    assert_eq!(find(&g, "ep").state, "Guidanz");
    assert_eq!(find(&g, "def").state, "DO NOT DISABLE");
    assert_eq!(
        find(&g, "def").short,
        "Must stay enabled. This app will not add an off switch."
    );
    assert_eq!(g.iter().filter(|r| r.state != "Guidanz").count(), 1);
    assert!(g.iter().all(|r| !r.detail.is_empty()));
}
