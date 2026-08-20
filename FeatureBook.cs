using System.Collections.Generic;

namespace J1939Reader
{
    internal sealed class SwitchRow
    {
        public string Id;
        public string Name;
        public string State;
        public string Short;
        public string Detail;
        public override string ToString() { return State.PadRight(10) + "  " + Name + "  —  " + Short; }
    }

    internal static class FeatureBook
    {
        public static List<SwitchRow> Live(bool connected, bool red, bool amber, bool hasInducement,
            bool hasTankFmi9, bool has1569, double rpm, string defTxt)
        {
            var list = new List<SwitchRow>();
            if (!connected)
            {
                list.Add(Row("link", "INLINE connection", "OFF",
                    "Not talking to the ECM.",
                    "Connect with Guidanz / J1939 tool / USB-Link Explorer closed. This row is live status, not a switch you flip."));
                return list;
            }

            list.Add(Row("red", "Red Stop lamp", red ? "ON" : "off",
                red ? "ECM is commanding no-fuel / shutdown." : "No Red Stop — no lamp-only reset needed.",
                "Red Stop is NOT its own code. It is bit 4–5 of DM1 byte 0. The ECM turns it ON when a red-stop DTC is active (on this unit: SPN 5246 inducement, 1569 protection, 5245 timer).\r\n\r\nReset all codes after the fault is gone and this lamp goes off by itself.\r\nThere is no legal 'Red Stop off' toggle. Forcing the lamp off while those SPNs are still active would hide a no-fuel command, not tell you if the repair worked."));

            list.Add(Row("amber", "Amber warning lamp", amber ? "ON" : "off",
                amber ? "A warning / derate path is active." : "No amber warning.",
                "Amber is the DM1 warning lamp (bits 2–3), not a separate DTC. On this machine it is usually the DEF tank FMI 9s (1761/3031/3364).\r\n\r\nIt clears when those SPNs go inactive and you reset. No independent Amber-off switch — same lamp byte as Red Stop."));

            list.Add(Row("induc", "SCR inducement (SPN 5246)", hasInducement ? "LOCKED" : "off",
                hasInducement ? "Most-severe inducement — will not fuel." : "Not reporting severe inducement.",
                "T4F aftertreatment protection. Stages go warning → derate → idle/no-restart. LOCKED means the ECM is protecting the SCR by refusing to fuel. Keep DEF working. Do not add a switch to disable this. After the tank header is really on the bus, Guidanz aftertreatment reset is the legal way to unlock it."));

            list.Add(Row("tank", "DEF tank header (level/temp/quality)", hasTankFmi9 ? "NOT TALKING" : "check data",
                hasTankFmi9 ? "FMI 9 on 1761/3031/3364 — module offline." : "No FMI 9 on tank SPNs in the last DM1.",
                "The tank sender reports DEF level, temperature, and quality. The DEF pump is a different part. NOT TALKING means power/ground/CAN at the header, not 'wrong percentage'. DEF fluid in the tank does not count until this row is healthy. Disabling this sender lets the ECM dose blind and can melt the SCR."));

            list.Add(Row("defval", "DEF tank broadcast", string.IsNullOrEmpty(defTxt) ? "—" : defTxt,
                "What PGN FE56 is actually publishing.",
                "n/a (not talking) matches FMI 9. A real percentage and temp means the header is alive. That is the gate before a code clear / Guidanz reset will stick."));

            list.Add(Row("prot", "Engine protection derate (SPN 1569)", has1569 ? "ON" : "off",
                has1569 ? "Torque/fuel limited or no-start from protection logic." : "Not active in DM1.",
                "Result of oil / coolant / DEF-empty / inducement logic. Turning 'engine protection' off in Guidanz (industrial password) is a real Features & Parameters item — it does NOT belong as a hidden DEF bypass. This app will not write that parameter. Fix the cause, then clear."));

            string run = rpm < 0 ? "—" : (rpm >= 400 ? "RUNNING" : (rpm >= 50 ? "CRANKING" : "STOPPED"));
            list.Add(Row("run", "Engine speed state", run,
                rpm < 0 ? "No EEC1 yet." : ("RPM " + rpm.ToString("0")),
                "From EEC1. STOPPED = key on, not spinning. CRANKING = starter rolling. RUNNING = fired. This is status only."));

            return list;
        }

        public static List<SwitchRow> GuidanzFeatures()
        {
            return new List<SwitchRow>
            {
                Row("ep", "Engine Protection Shutdown", "Guidanz",
                    "Industrial feature: stop the engine on low oil / high coolant / etc.",
                    "Cummins Features & Parameters (INSITE/Guidanz), not a J1939 lamp bit this app can flip.\r\n\r\nON (normal): ECM shuts down or derates when oil pressure, coolant temp, or similar protections trip. That saves the engine.\r\n\r\nOFF: engine may keep running through a protection fault. Some industrial calibrations allow this with a dealer password for specific applications. Turning it off is not a DEF workaround and will not clear SPN 5246.\r\n\r\nThis app does not write this switch. Use Guidanz with a Cummins login if the shop process calls for it."),

                Row("lop", "Low Oil Pressure Shutdown", "Guidanz",
                    "Subset of engine protection.",
                    "When ON, low oil pressure at speed will stop or derate the engine. At 0 RPM, oil pressure is normally 0 — that is not this trip. Do not disable to mask a real oil-pressure fault."),

                Row("hct", "High Coolant Temperature Shutdown", "Guidanz",
                    "Subset of engine protection.",
                    "When ON, overheat will derate/stop. Coolant near ambient with the engine sitting is normal. Not related to DEF inducement."),

                Row("idle", "Idle Shutdown Timer", "Guidanz",
                    "Shuts the engine off after a set idle time.",
                    "Saves fuel on a compressor that was left idling. If it is ON and too short, the unit can die after warmup and look like a no-start on the next crank. It does not set Red Stop or 5246. Adjust only in Guidanz."),

                Row("warm", "Engine Warm-Up / Idle Hold", "Guidanz",
                    "Holds elevated idle until coolant is up.",
                    "Normal on T4 industrial. Not a no-start lock. If it will not leave idle after it is warm, that is a different fault (VGT, EGR, inducement)."),

                Row("pto", "PTO / compressor speed control", "Guidanz",
                    "How the ECM answers speed requests from the compressor controller.",
                    "On a Bobcat/Doosan portable the compressor controller (SA 48) asks for RPM on J1939. PTO/speed-control features in Guidanz change how that request is honored. Wrong settings can mean 'cranks but will not run up' after it fires — not Red Stop during crank. Do not confuse with DEF inducement."),

                Row("fan", "Fan Control", "Guidanz",
                    "ECM-controlled cooling fan.",
                    "On/off or variable fan. A fan stuck on or off is a heat/noise issue, not a crank/no-fire."),

                Row("acc", "Accelerator type / throttle source", "Guidanz",
                    "Pedal vs J1939 vs switched idle.",
                    "Portable compressors usually have no foot pedal; the compressor controller is the throttle. If this is set to 'pedal' and there is no pedal, it may not fuel after start. Still not a DEF tank FMI 9."),

                Row("max", "Maximum / low idle speed", "Guidanz",
                    "RPM governors.",
                    "Limits how fast or slow it will run after it fires. Will not create a no-inject Red Stop during crank."),

                Row("start", "Starter lockout / restart delay", "Guidanz",
                    "Prevents grinding the starter.",
                    "Can block a second crank too soon. If the starter is already spinning the engine (RPM 150+), this is not your no-fire."),

                Row("def", "Aftertreatment / DEF / SCR", "DO NOT DISABLE",
                    "Must stay enabled. This app will not add an off switch.",
                    "DEF quality, level, temp, pump, and dosing are how a T4F Cummins keeps the SCR brick in range.\r\n\r\nIf you disable these 'switches' in a tune or with a delete:\r\n- Inducement logic is gone but so is dosing control\r\n- SCR catalyst and DPF can overheat or plug\r\n- Next regen or limp event can melt aftertreatment or push debris into the turbo\r\n- You also fail emissions law for tampering\r\n\r\nThis machine must keep DEF working. The correct path is: make the tank header talk (real level/temp/quality), then clear / Guidanz aftertreatment reset. There is no safe off switch here."),

                Row("vgt", "VGT / turbo actuator", "Guidanz",
                    "Variable geometry turbo position control.",
                    "Stuck VGT can smoke, lack power, or hard-start when hot. It is not the FMI 9 tank-header bundle. Diagnose in Guidanz after the engine will fire."),

                Row("egr", "EGR valve", "Guidanz",
                    "Exhaust gas recirculation.",
                    "Stuck EGR can rough-run or derate. Separate from DEF tank sender. Do not disable EGR as a 'fix' for inducement."),
            };
        }

        static SwitchRow Row(string id, string name, string state, string sh, string detail)
        {
            return new SwitchRow { Id = id, Name = name, State = state, Short = sh, Detail = detail };
        }
    }
}
