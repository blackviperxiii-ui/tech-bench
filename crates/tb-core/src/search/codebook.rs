//! Fault explainer text for one SPN/FMI (port of `src/Features/Search/CodeBook.cs`).
//! Text is copied verbatim from the C# app; lines end in CRLF like `StringBuilder.AppendLine` on Windows.

use crate::j1939::decode::fmi_name;
use crate::j1939::Names;

const NL: &str = "\r\n";

/// The full explainer shown for a DTC: title, SPN/FMI line, body, FMI meaning, start impact, checks.
pub fn explain(spn: i32, fmi: i32, names: &Names) -> String {
    let mut sb = String::new();
    let mut line = |s: &str| {
        sb.push_str(s);
        sb.push_str(NL);
    };
    line(&title(spn, names));
    line(&format!("SPN {spn}   FMI {fmi} — {}", fmi_name(fmi)));
    line("");
    line(&body(spn));
    line("");
    line("This FMI means:");
    line(&fmi_body(fmi));
    line("");
    line("Will it prevent start / fueling?");
    line(start_impact(spn));
    line("");
    line("What to check:");
    line(checks(spn));
    sb
}

fn title(spn: i32, names: &Names) -> String {
    let n = names.spn(spn);
    if n.is_empty() {
        format!("SPN {spn}")
    } else {
        n
    }
}

fn body(spn: i32) -> String {
    match spn {
        94 => "Low-pressure fuel after the lift pump / gear pump, before the high-pressure pump. Not rail pressure. A real number here means supply fuel is present; 0xFF means the ECM is not publishing a valid pressure.",
        97 => "Water-in-fuel sensor in the separator. On = drain the bowl. Open/short is wiring or the probe.",
        100 => "Engine oil pressure. At 0 RPM this is often 0 and is not a cranking-oil-pressure fault by itself. While running, low oil pressure can shut the engine down if engine protection is enabled.",
        110 => "Engine coolant temperature. ~ambient with key-on and RPM 0 is normal. High coolant can derate or shut down.",
        157 => "High-pressure fuel rail. Stays near 0 with the engine not spinning. During crank it must rise or the ECM will not inject. Do not crack common-rail lines.",
        168 => "ECM battery voltage. Cranking sag below ~10 V (12 V system) or ~18 V (24 V) can stop injector drivers even if RPM looks OK.",
        190 => "Engine speed from the crank sensor. 150–250 RPM while cranking is enough to start. 0 RPM with the starter spinning points at the crank sensor / tone wheel, not fuel.",
        639 => "J1939 datalink health as seen by the ECM. Abnormal update rate means a module dropped off or the bus is noisy.",
        677 => "Starter relay control. Fault here is starter command, not 'will not fire' after it is already rolling.",
        723 => "Camshaft position. Crank RPM can still look good if cam is dead. Many Cummins will not inject without cam/crank sync.",
        1079 | 1080 => "5 V sensor supply. If this is shorted, several analog sensors fail at once (oil, coolant, rail, etc.).",
        1569 => "Engine protection torque derate / DEF-empty related shutdown. On T4F Cummins this often rides along with SCR inducement when the ECM believes DEF is empty or the tank electronics are dead. It is a result code, not the root cause. Fix the DEF tank data (level/temp/quality actually updating), then this can drop.",
        1761 => "DEF (diesel exhaust fluid) tank level sender. This is the tank header electronics, not the DEF pump. If the pump runs but this SPN is FMI 9, the ECM is not receiving tank-level messages — plug, power, ground, or the tank module. Fluid in the tank does not satisfy this by itself.",
        3031 => "DEF tank temperature sender, same header as level. FMI 9 with 1761 and 3364 together almost always means the whole tank module is offline, not that DEF is cold.",
        3361 => "DEF dosing unit (the injector in the exhaust). Separate from the tank header. Pump/dosing faults are not the same as tank level/temp/quality FMI 9.",
        3364 => "DEF quality sensor (urea concentration) in the tank header. FMI 9 = no updates. A 'wrong reading' would be FMI 2/16/18 with an actual percentage on the bus. Do not disable this sensor — the ECM uses it to dose correctly. Wrong dose can overheat the SCR brick or lock inducement permanently.",
        4331 | 4334 => "DEF system pressure at the doser. Pump running does not equal this SPN healthy. Line leaks, pump, or doser.",
        5245 => "SCR operator inducement timer. Counts how far the ECM has gone in the warning → derate → no-restart path. It is a consequence of other DEF/SCR faults, not a sensor you replace.",
        5246 => "SCR operator inducement severity. FMI 0 = most severe (Red Stop / no fuel). This is the lock that keeps a live ECM from injecting. Clearing it without the tank sensors talking does nothing useful — it relights immediately. After the tank is really on the bus, Guidanz aftertreatment reset is often still required. Never 'turn off DEF' to get past this; that is how you melt a DPF/SCR and grenade a T4 engine.",
        5392 | 5394 => "DEF pump command/state. A new pump does not clear tank-header FMI 9 codes. Pump and tank sender are different parts.",
        _ => return format!(
            "Cummins J1939 diagnostic parameter. SPN is the item; FMI is how it failed. Use the FMI text plus the checks below. If this SPN is not in the book, treat it as 'see Cummins troubleshooting for SPN {spn}'."
        ),
    }
    .to_string()
}

fn fmi_body(fmi: i32) -> String {
    match fmi {
        0 | 1 => "Value is out of range in the most severe band. The sensor is talking; the number is too high or too low.",
        2 => "Sensor is talking but the value is jumping / implausible. Wiring noise, bad sender, or intermittent connector.",
        3 => "Signal voltage shorted high (usually 5 V or battery). Wiring to power or open on a pull-up input.",
        4 => "Signal voltage shorted low (ground). Pinched wire or failed sender.",
        9 => "Abnormal update rate — the ECM is not receiving messages from that device. This is NOT a 'wrong number'. It is a dead/missing module, unplugged header, lost power/ground, or a CAN wire at that component. Clearing the code will not make the engine run until updates resume.",
        16 | 18 => "Moderate over/under range. Sensor is alive; reading is off. Fix/replace the sender — do not disable it.",
        31 => "Condition exists. Often a status/result (inducement, derate, empty). Look for the sensor SPNs that caused it.",
        _ => return format!("{}.", fmi_name(fmi)),
    }
    .to_string()
}

fn start_impact(spn: i32) -> &'static str {
    match spn {
        5246 | 1569 | 5245 => "YES. This is in the no-fuel / Red Stop path on T4F. The engine can crank, show RPM, and still not inject.",
        1761 | 3031 | 3364 => "Indirectly. These do not inject fuel themselves, but they feed inducement (5246). If they stay FMI 9, Red Stop stays on.",
        190 | 723 => "Can. No crank RPM or no cam/crank sync = no inject.",
        157 | 94 => "Can, if rail/supply never builds while cranking.",
        _ => "Usually not by itself. Watch whether Red Stop / 5246 is also active.",
    }
}

fn checks(spn: i32) -> &'static str {
    match spn {
        1761 | 3031 | 3364 => "- DEF tank header connector: power, ground, CAN high/low with key ON\r\n- All three FMI 9 together = whole module offline, not 'low DEF'\r\n- After it talks, DEF % and temp should be real numbers (not n/a)\r\n- Then clear codes / Guidanz aftertreatment reset. Do not disable the sender.",
        5246 | 5245 | 1569 => "- Find the DEF tank SPNs (1761/3031/3364) first\r\n- Confirm DEF level/temp become real values\r\n- Then Guidanz aftertreatment reset — a generic clear often will not drop 5246\r\n- Do not turn DEF/SCR off; that wrecks the aftertreatment and the engine.",
        190 => "- Crank sensor air gap / connector / tone wheel\r\n- Watch RPM while cranking in this app (need ~150+)",
        723 => "- Cam sensor connector and oil on the pin\r\n- RPM can look fine from crank while cam is dead",
        _ => "- Confirm whether the SPN is n/a (not talking) vs a real out-of-range number\r\n- Connector, power, ground, then the sender\r\n- Do not disable emissions or protection sensors to force a start",
    }
}
