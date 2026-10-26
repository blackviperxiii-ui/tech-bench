//! SHA-256 against the FIPS 180-4 / NIST example digests, plus the hex form shop sync stores.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::sha256::{digest, hash_text, hex, Sha256};

#[test]
fn known_digests() {
    assert_eq!(
        hash_text(""),
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    );
    assert_eq!(
        hash_text("abc"),
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    );
    assert_eq!(
        hash_text("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq"),
        "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1"
    );
    assert_eq!(
        hex(&digest(&vec![b'a'; 1_000_000])),
        "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0"
    );
    assert_eq!(
        hash_text("hi"),
        "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4"
    );
}

#[test]
fn chunked_update_matches_one_shot() {
    let data: Vec<u8> = (0..1000u32).map(|i| (i * 7 % 251) as u8).collect();
    for step in [1, 3, 55, 56, 63, 64, 65, 127, 999] {
        let mut h = Sha256::new();
        for chunk in data.chunks(step) {
            h.update(chunk);
        }
        assert_eq!(h.finish(), digest(&data), "step {step}");
    }
}

#[test]
fn padding_edges() {
    // 55, 56 and 64 bytes sit on the one-block / two-block padding boundary.
    assert_eq!(
        hash_text(&"a".repeat(55)),
        "9f4390f8d30c2dd92ec9f095b65e2b9ae9b0a925a5258e241c9f1e910f734318"
    );
    assert_eq!(
        hash_text(&"a".repeat(56)),
        "b35439a4ac6f0948b6d6f9e3c6af0f5f590ce20f1bde7090ef7970686ec6738a"
    );
    assert_eq!(
        hash_text(&"a".repeat(64)),
        "ffe054fe7ae0cb6dc65c3af9b61d5209f439851db43d0ba5997337df154668eb"
    );
}
