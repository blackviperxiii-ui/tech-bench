//! Ported from tests/SelfTest.Ini.cs with the same literal expected values.

// Under wasm-pack the same cases run through wasm-bindgen-test.
#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::ini::Ini;

fn p(text: &str) -> Ini {
    Ini::parse(Some(text))
}

#[test]
fn ini_parse_and_lookup_helpers() {
    let none = Ini::parse(None);
    assert!(!none.has_section(Some("A")));
    assert!(!none.has_section(Some("")));
    assert_eq!(none.get(Some("A"), Some("k")), "");
    let empty = p("");
    assert!(!empty.has_section(Some("A")));
    assert_eq!(empty.get(Some("A"), Some("k")), "");
    assert_eq!(empty.get_int("A", "k", 7), 7);
    assert_eq!(empty.get_list("A", "k").len(), 0);
    assert_eq!(empty.get_int_list("A", "k").len(), 0);

    let c = p("; top comment\n# hash comment\n\n   \n[S]\n; k=skipped\n# k2=skipped\na=1\n\n  ;indented=skipped\nb=2\n");
    assert_eq!(c.get(Some("S"), Some("k")), "");
    assert_eq!(c.get(Some("S"), Some("k2")), "");
    assert_eq!(c.get(Some("S"), Some("indented")), "");
    assert_eq!(
        format!(
            "{},{}",
            c.get(Some("S"), Some("a")),
            c.get(Some("S"), Some("b"))
        ),
        "1,2"
    );
    assert_eq!(p("[S]\r\nx=1\ry=2\r\n").get(Some("S"), Some("y")), "2");

    let q = p("[S]\nq=\"hello world\"\nsp=\"  padded  \"\nlone=\"x\nempty=\"\"\none=\"\nmid=a\"b\"c\neq=a=b\nsp2 =  v  \n");
    let g = |k: &str| q.get(Some("S"), Some(k));
    assert_eq!(g("q"), "hello world");
    assert_eq!(g("sp"), "  padded  ");
    assert_eq!(g("lone"), "\"x");
    assert_eq!(g("empty"), "");
    assert_eq!(g("one"), "\"");
    assert_eq!(g("mid"), "a\"b\"c");
    assert_eq!(g("eq"), "a=b");
    assert_eq!(g("sp2"), "v");

    let ci = p("[VendorInformation]\nName=Cummins\n");
    assert_eq!(ci.get(Some("vendorinformation"), Some("Name")), "Cummins");
    assert_eq!(ci.get(Some("VENDORINFORMATION"), Some("NAME")), "Cummins");
    assert!(ci.has_section(Some("VENDORINFORMATION")));
    assert!(!ci.has_section(Some("Other")));
    assert!(ci.has_section(None), "None is the sectionless bucket");
    assert_eq!(p("top=1\n[S]\nk=2\n").get(None, Some("top")), "1");
    assert_eq!(ci.get(Some("VendorInformation"), None), "");
    assert_eq!(ci.get(Some("VendorInformation"), Some("Nope")), "");
    assert_eq!(ci.get(Some("Nope"), Some("Name")), "");

    let d = p("[A]\nx=1\ny=old\n[B]\nx=b\n[a]\ny=new\nz=3\n");
    assert_eq!(d.get(Some("A"), Some("x")), "1");
    assert_eq!(d.get(Some("A"), Some("z")), "3");
    assert_eq!(d.get(Some("A"), Some("y")), "new");
    assert_eq!(d.get(Some("B"), Some("x")), "b");
    assert_eq!(p("top=1\n[S]\nk=2\n").get(Some(""), Some("top")), "1");
    assert_eq!(p("[S]\n=v\nnoequals\nk=1\n").get(Some("S"), Some("")), "");
    assert_eq!(p("[Open\nk=1\n").get(Some("Open"), Some("k")), "1");
    assert_eq!(p("[ Spaced ]\nk=1\n").get(Some("Spaced"), Some("k")), "1");

    let n = p("[S]\ni=42\nneg= -5 \nbad=abc\nblank=\nbig=99999999999\n");
    assert_eq!(n.get_int("S", "i", -1), 42);
    assert_eq!(n.get_int("S", "neg", 0), -5);
    assert_eq!(n.get_int("S", "bad", 9), 9);
    assert_eq!(n.get_int("S", "blank", 8), 8);
    assert_eq!(n.get_int("S", "big", 6), 6);
    assert_eq!(n.get_int("S", "none", 5), 5);
    assert_eq!(n.get_int("Z", "i", 4), 4);

    let l = p("[S]\nlist= a , ,b,, c ,\nrates=250, 500,250,x,,500, 1000\nnone=\n");
    assert_eq!(l.get_list("S", "list").join("|"), "a|b|c");
    assert_eq!(l.get_list("S", "none").len(), 0);
    assert_eq!(l.get_list("S", "nope").len(), 0);
    assert_eq!(l.get_int_list("S", "rates"), vec![250, 500, 1000]);
    assert_eq!(l.get_int_list("S", "nope").len(), 0);
}
