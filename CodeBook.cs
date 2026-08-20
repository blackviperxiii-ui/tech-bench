using System.Text;

namespace J1939Reader
{
    internal static class CodeBook
    {
        public static string Explain(int spn, int fmi)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Title(spn));
            sb.AppendLine("SPN " + spn + "   FMI " + fmi + " — " + J1939Decode.FmiName(fmi));
            sb.AppendLine();
            sb.AppendLine(Body(spn));
            sb.AppendLine();
            sb.AppendLine("This FMI means:");
            sb.AppendLine(FmiBody(fmi));
            sb.AppendLine();
            sb.AppendLine("Will it prevent start / fueling?");
            sb.AppendLine(StartImpact(spn, fmi));
            sb.AppendLine();
            sb.AppendLine("What to check:");
            sb.AppendLine(Checks(spn, fmi));
            return sb.ToString();
        }

        static string Title(int spn)
        {
            string n = J1939Decode.SpnName(spn);
            return string.IsNullOrEmpty(n) ? ("SPN " + spn) : n;
        }

        static string Body(int spn)
        {
            switch (spn)
            {
                case 94:
                    return "Low-pressure fuel after the lift pump / gear pump, before the high-pressure pump. Not rail pressure. A real number here means supply fuel is present; 0xFF means the ECM is not publishing a valid pressure.";
                case 97:
                    return "Water-in-fuel sensor in the separator. On = drain the bowl. Open/short is wiring or the probe.";
                case 100:
                    return "Engine oil pressure. At 0 RPM this is often 0 and is not a cranking-oil-pressure fault by itself. While running, low oil pressure can shut the engine down if engine protection is enabled.";
                case 110:
                    return "Engine coolant temperature. ~ambient with key-on and RPM 0 is normal. High coolant can derate or shut down.";
                case 157:
                    return "High-pressure fuel rail. Stays near 0 with the engine not spinning. During crank it must rise or the ECM will not inject. Do not crack common-rail lines.";
                case 168:
                    return "ECM battery voltage. Cranking sag below ~10 V (12 V system) or ~18 V (24 V) can stop injector drivers even if RPM looks OK.";
                case 190:
                    return "Engine speed from the crank sensor. 150–250 RPM while cranking is enough to start. 0 RPM with the starter spinning points at the crank sensor / tone wheel, not fuel.";
                case 639:
                    return "J1939 datalink health as seen by the ECM. Abnormal update rate means a module dropped off or the bus is noisy.";
                case 677:
                    return "Starter relay control. Fault here is starter command, not 'will not fire' after it is already rolling.";
                case 723:
                    return "Camshaft position. Crank RPM can still look good if cam is dead. Many Cummins will not inject without cam/crank sync.";
                case 1079:
                case 1080:
                    return "5 V sensor supply. If this is shorted, several analog sensors fail at once (oil, coolant, rail, etc.).";
                case 1569:
                    return "Engine protection torque derate / DEF-empty related shutdown. On T4F Cummins this often rides along with SCR inducement when the ECM believes DEF is empty or the tank electronics are dead. It is a result code, not the root cause. Fix the DEF tank data (level/temp/quality actually updating), then this can drop.";
                case 1761:
                    return "DEF (diesel exhaust fluid) tank level sender. This is the tank header electronics, not the DEF pump. If the pump runs but this SPN is FMI 9, the ECM is not receiving tank-level messages — plug, power, ground, or the tank module. Fluid in the tank does not satisfy this by itself.";
                case 3031:
                    return "DEF tank temperature sender, same header as level. FMI 9 with 1761 and 3364 together almost always means the whole tank module is offline, not that DEF is cold.";
                case 3361:
                    return "DEF dosing unit (the injector in the exhaust). Separate from the tank header. Pump/dosing faults are not the same as tank level/temp/quality FMI 9.";
                case 3364:
                    return "DEF quality sensor (urea concentration) in the tank header. FMI 9 = no updates. A 'wrong reading' would be FMI 2/16/18 with an actual percentage on the bus. Do not disable this sensor — the ECM uses it to dose correctly. Wrong dose can overheat the SCR brick or lock inducement permanently.";
                case 4331:
                case 4334:
                    return "DEF system pressure at the doser. Pump running does not equal this SPN healthy. Line leaks, pump, or doser.";
                case 5245:
                    return "SCR operator inducement timer. Counts how far the ECM has gone in the warning → derate → no-restart path. It is a consequence of other DEF/SCR faults, not a sensor you replace.";
                case 5246:
                    return "SCR operator inducement severity. FMI 0 = most severe (Red Stop / no fuel). This is the lock that keeps a live ECM from injecting. Clearing it without the tank sensors talking does nothing useful — it relights immediately. After the tank is really on the bus, Guidanz aftertreatment reset is often still required. Never 'turn off DEF' to get past this; that is how you melt a DPF/SCR and grenade a T4 engine.";
                case 5392:
                case 5394:
                    return "DEF pump command/state. A new pump does not clear tank-header FMI 9 codes. Pump and tank sender are different parts.";
                default:
                    return "Cummins J1939 diagnostic parameter. SPN is the item; FMI is how it failed. Use the FMI text plus the checks below. If this SPN is not in the book, treat it as 'see Cummins troubleshooting for SPN " + spn + "'.";
            }
        }

        static string FmiBody(int fmi)
        {
            switch (fmi)
            {
                case 0:
                case 1:
                    return "Value is out of range in the most severe band. The sensor is talking; the number is too high or too low.";
                case 2:
                    return "Sensor is talking but the value is jumping / implausible. Wiring noise, bad sender, or intermittent connector.";
                case 3:
                    return "Signal voltage shorted high (usually 5 V or battery). Wiring to power or open on a pull-up input.";
                case 4:
                    return "Signal voltage shorted low (ground). Pinched wire or failed sender.";
                case 9:
                    return "Abnormal update rate — the ECM is not receiving messages from that device. This is NOT a 'wrong number'. It is a dead/missing module, unplugged header, lost power/ground, or a CAN wire at that component. Clearing the code will not make the engine run until updates resume.";
                case 16:
                case 18:
                    return "Moderate over/under range. Sensor is alive; reading is off. Fix/replace the sender — do not disable it.";
                case 31:
                    return "Condition exists. Often a status/result (inducement, derate, empty). Look for the sensor SPNs that caused it.";
                default:
                    return J1939Decode.FmiName(fmi) + ".";
            }
        }

        static string StartImpact(int spn, int fmi)
        {
            if (spn == 5246 || spn == 1569 || spn == 5245)
                return "YES. This is in the no-fuel / Red Stop path on T4F. The engine can crank, show RPM, and still not inject.";
            if (spn == 1761 || spn == 3031 || spn == 3364)
                return "Indirectly. These do not inject fuel themselves, but they feed inducement (5246). If they stay FMI 9, Red Stop stays on.";
            if (spn == 190 || spn == 723)
                return "Can. No crank RPM or no cam/crank sync = no inject.";
            if (spn == 157 || spn == 94)
                return "Can, if rail/supply never builds while cranking.";
            return "Usually not by itself. Watch whether Red Stop / 5246 is also active.";
        }

        static string Checks(int spn, int fmi)
        {
            switch (spn)
            {
                case 1761:
                case 3031:
                case 3364:
                    return "- DEF tank header connector: power, ground, CAN high/low with key ON\r\n- All three FMI 9 together = whole module offline, not 'low DEF'\r\n- After it talks, DEF % and temp should be real numbers (not n/a)\r\n- Then clear codes / Guidanz aftertreatment reset. Do not disable the sender.";
                case 5246:
                case 5245:
                case 1569:
                    return "- Find the DEF tank SPNs (1761/3031/3364) first\r\n- Confirm DEF level/temp become real values\r\n- Then Guidanz aftertreatment reset — a generic clear often will not drop 5246\r\n- Do not turn DEF/SCR off; that wrecks the aftertreatment and the engine.";
                case 190:
                    return "- Crank sensor air gap / connector / tone wheel\r\n- Watch RPM while cranking in this app (need ~150+)";
                case 723:
                    return "- Cam sensor connector and oil on the pin\r\n- RPM can look fine from crank while cam is dead";
                default:
                    return "- Confirm whether the SPN is n/a (not talking) vs a real out-of-range number\r\n- Connector, power, ground, then the sender\r\n- Do not disable emissions or protection sensors to force a start";
            }
        }
    }
}
