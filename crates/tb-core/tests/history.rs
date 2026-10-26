//! Unit history: `HistoryTests` in SelfTest.cs, and the history publish / import half of the
//! shop sync tests there.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::history::{job_matches, split_csv, HistoryFault};

#[test]
fn csv_split_and_job_match() {
    assert_eq!(split_csv("a,\"b,c\",d").join("|"), "a|b,c|d");
    assert_eq!(split_csv("\"say \"\"hi\"\"\",x").join("|"), "say \"hi\"|x");
    assert_eq!(split_csv("a,").len(), 2, "csv split handles trailing empty");

    assert!(
        job_matches("HP450  12345", "12345"),
        "job matches on serial token"
    );
    assert!(
        job_matches("HP450  12345", "hp450 12345"),
        "job matches on both tokens"
    );
    assert!(
        !job_matches("HP450  12345", "99999"),
        "job rejects a different serial"
    );
    assert!(
        job_matches("HP450  12345", "hp450 ab"),
        "short tokens ignored"
    );
    assert!(job_matches("anything", ""), "empty query matches");
    assert!(
        !job_matches("anything", "ab"),
        "only short tokens match nothing"
    );
}

#[test]
fn fault_line() {
    let f = HistoryFault {
        spn: 1761,
        fmi: 9,
        name: "DEF tank level".into(),
        visits: 2,
        first: 0,
        // 2026-04-01T09:00:00Z
        last: 17_750_340_000_000_000,
    };
    assert_eq!(
        f.to_string(),
        "SPN 1761   FMI 9   seen on 2 visits   last 2026-04-01  DEF tank level"
    );
}

#[cfg(not(target_arch = "wasm32"))]
mod fs_tests {
    use std::fs;
    use std::path::PathBuf;
    use tb_core::history::{date_of, History};
    use tb_core::kb::shop::shard_dir;
    use tb_core::kb::sync::{import_history, publish_history};
    use tb_core::sha256::hash_file;

    fn temp(tag: &str) -> PathBuf {
        let p = std::env::temp_dir().join(format!("tb-hist-{tag}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&p);
        fs::create_dir_all(&p).unwrap();
        p
    }

    const HEADER: &str = "time,job,state,spn,fmi,occurrences,name,fmi_text";

    #[test]
    fn unit_history() {
        let dir = temp("unit");
        fs::write(
            dir.join("HP450_12345_session_20260101_090000_dtcs.csv"),
            format!("{HEADER}\n2026-01-01T09:00:00,\"HP450 12345\",active,1761,9,3,\"DEF tank level\",\"abnormal update rate\"\n2026-01-01T09:00:00,\"HP450 12345\",active,5246,0,1,\"SCR inducement\",\"above normal\"\n"),
        )
        .unwrap();
        fs::write(
            dir.join("HP450_12345_session_20260401_090000_dtcs.csv"),
            format!("{HEADER}\n2026-04-01T09:00:00,\"HP450 12345\",active,1761,9,7,\"DEF tank level\",\"abnormal update rate\"\n"),
        )
        .unwrap();
        fs::write(
            dir.join("OTHER_999_session_20260401_100000_dtcs.csv"),
            format!("{HEADER}\n2026-04-01T10:00:00,\"OTHER 999\",active,100,1,1,\"Oil pressure\",\"below normal\"\n"),
        )
        .unwrap();
        fs::write(dir.join("broken_dtcs.csv"), "not a csv at all").unwrap();
        fs::write(dir.join("notes.txt"), "not history").unwrap();

        let h = History::load(&dir);
        assert_eq!(h.files_read, 4, "all dtc files read");
        assert_eq!(h.entries.len(), 4, "entries parsed");

        let faults = h.for_job("HP450 12345");
        assert_eq!(faults.len(), 2, "faults for this unit only");
        assert_eq!(faults[0].spn, 1761, "repeat offender first");
        assert_eq!(faults[0].visits, 2, "counted across two visits");
        assert_eq!(faults[1].visits, 1, "one-off counted once");
        assert!(
            faults[0].first < faults[0].last,
            "first seen is the earlier visit"
        );
        assert_eq!(date_of(faults[0].first), "2026-01-01");

        assert_eq!(h.for_job("OTHER 999").len(), 1, "other unit is separate");
        assert_eq!(h.for_job("NOPE 000").len(), 0, "unknown unit has nothing");
        assert_eq!(
            h.jobs(),
            vec!["OTHER 999".to_string(), "HP450 12345".to_string()]
        );

        assert_eq!(
            h.summary("HP450 12345"),
            "This unit has been here before — 2 distinct fault(s) on file:\n\n  SPN 1761   FMI 9   seen on 2 visits   last 2026-04-01  DEF tank level\n      repeat offender — first seen 2026-01-01\n  SPN 5246   FMI 0   seen on 1 visit   last 2026-01-01  SCR inducement\n"
        );
        assert_eq!(
            h.summary("NOPE 000"),
            "Nothing on file for \"NOPE 000\".\n4 session file(s) indexed.\n"
        );
        assert_eq!(
            h.summary(""),
            "Type a model/serial in the job strip to see that unit's history.\n4 saved session file(s) indexed, 2 job(s).\n"
        );

        let none = History::load(&dir.join("nope"));
        assert_eq!(none.files_read, 0, "missing folder reads nothing");
        assert_eq!(
            none.summary("x"),
            "No saved sessions yet.\n\nPress Save on the INLINE 7 tab and this unit's faults become searchable history.\n"
        );
    }

    #[test]
    fn publish_and_import() {
        let root = temp("import");
        let kb = root.join("kb");
        let sessions = root.join("sessions");
        let src = root.join("job_dtcs.csv");
        fs::write(&src, format!("{HEADER}\n2026,HP,active,1,0,1,x,x\n")).unwrap();
        publish_history(&kb, "alice", &src).unwrap();
        let hist = shard_dir(&kb, "alice").join("history");
        assert!(hist.join("job_dtcs.csv").is_file());

        assert_eq!(
            import_history(&kb, &sessions).unwrap(),
            1,
            "import copies missing history"
        );
        assert_eq!(
            import_history(&kb, &sessions).unwrap(),
            0,
            "import is idempotent"
        );

        fs::write(sessions.join("job_dtcs.csv"), "local-only").unwrap();
        fs::write(hist.join("job_dtcs.csv"), "shop-other").unwrap();
        assert_eq!(
            import_history(&kb, &sessions).unwrap(),
            1,
            "same name, new body imports"
        );
        assert_eq!(
            fs::read_to_string(sessions.join("job_dtcs.csv")).unwrap(),
            "local-only"
        );
        let h = hash_file(&hist.join("job_dtcs.csv")).unwrap();
        let collision = sessions.join(format!("job.alice-{}_dtcs.csv", &h[..8]));
        assert_eq!(fs::read_to_string(&collision).unwrap(), "shop-other");
        assert_eq!(
            History::load(&sessions).files_read,
            2,
            "history sees the collision file"
        );
        assert_eq!(
            import_history(&kb, &sessions).unwrap(),
            0,
            "second import returns 0"
        );

        fs::write(hist.join("dup_dtcs.csv"), "shop-other").unwrap();
        assert_eq!(
            import_history(&kb, &sessions).unwrap(),
            0,
            "same hash under another name"
        );

        fs::write(hist.join("clash_dtcs.csv"), "shop-body-v2").unwrap();
        let ch = hash_file(&hist.join("clash_dtcs.csv")).unwrap();
        fs::write(sessions.join("clash_dtcs.csv"), "local-body").unwrap();
        let short = sessions.join(format!("clash.alice-{}_dtcs.csv", &ch[..8]));
        fs::write(&short, "other-bytes").unwrap();
        assert_eq!(import_history(&kb, &sessions).unwrap(), 1);
        assert_eq!(
            fs::read_to_string(sessions.join(format!("clash.alice-{ch}_dtcs.csv"))).unwrap(),
            "shop-body-v2",
            "import lands under the full-hash name"
        );
        assert_eq!(
            fs::read_to_string(&short).unwrap(),
            "other-bytes",
            "8-hex file kept"
        );
    }
}
