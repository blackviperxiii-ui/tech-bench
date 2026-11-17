//! The INLINE 7 Shot file (`SessionIo.Screenshot` in src/Shell/Session.cs): shot_<stamp>.png.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::shot::{file_name, PNG_MAGIC};

#[test]
fn shot_file_name_matches_winforms() {
    assert_eq!(file_name("20260501_071500"), "shot_20260501_071500.png");
    assert_eq!(PNG_MAGIC, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
}

#[cfg(not(target_arch = "wasm32"))]
#[test]
fn save_writes_a_png_and_refuses_anything_else() {
    use tb_core::shot::save;
    let dir = std::env::temp_dir().join(format!("tb-shot-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    let mut png = PNG_MAGIC.to_vec();
    png.extend_from_slice(b"rest");
    let path = save(&dir, "20260501_071500", &png).unwrap();
    assert_eq!(path, dir.join("shot_20260501_071500.png"));
    assert_eq!(std::fs::read(&path).unwrap(), png);

    let e = save(&dir, "20260501_071501", b"GIF89a").unwrap_err();
    assert_eq!(e.to_string(), "The shot is not a PNG picture.");
    let e = save(&dir, "../evil", &png).unwrap_err();
    assert_eq!(e.to_string(), "The shot needs a yyyyMMdd_HHmmss stamp.");
    assert_eq!(
        std::fs::read_dir(&dir).unwrap().count(),
        1,
        "only the good shot was written"
    );
    std::fs::remove_dir_all(&dir).unwrap();
}
