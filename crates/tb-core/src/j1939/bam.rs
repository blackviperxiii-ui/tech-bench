//! Reassemble J1939 BAM (TP.CM 0xEC00 + TP.DT 0xEB00) into a full PGN, e.g. long DM1
//! (port of `src/Core/J1939/Bam.cs`). The caller passes the receive time in milliseconds
//! instead of the C# `DateTime.UtcNow`, so the same code runs on wasm and in tests.

use std::collections::HashMap;

/// One CAN frame as the adapter hands it up (C# `J1939Frame`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct J1939Frame {
    pub pgn: i32,
    pub sa: i32,
    /// Destination address.
    pub da: i32,
    pub data: Vec<u8>,
}

/// J1939 allows 750 ms between BAM packets; drop a transfer that stalls past this.
pub const STALE_MS: u64 = 2000;

#[derive(Debug)]
struct Transfer {
    pgn: i32,
    total: usize,
    packets: usize,
    buf: Vec<u8>,
    seen: Vec<bool>,
    got: usize,
    touched_ms: u64,
}

#[derive(Debug, Default)]
pub struct BamAssembler {
    map: HashMap<i32, Transfer>,
}

impl BamAssembler {
    pub fn new() -> Self {
        Self::default()
    }

    /// Feed one frame received at `now_ms`. Returns the frame to handle: the frame itself for a
    /// plain PGN, the reassembled message when a BAM completes, or `None` while a transfer is
    /// still in flight (and for TP frames that don't belong to one).
    pub fn feed(&mut self, f: &J1939Frame, now_ms: u64) -> Option<J1939Frame> {
        self.drop_stale(now_ms);

        if f.pgn == 0xEC00 && f.data.len() >= 8 && f.data[0] == 0x20 {
            let d = &f.data;
            let size = usize::from(d[1]) | (usize::from(d[2]) << 8);
            let pkts = usize::from(d[3]);
            let pgn = i32::from(d[5]) | (i32::from(d[6]) << 8) | (i32::from(d[7]) << 16);
            if !(1..=1785).contains(&size) || !(1..=255).contains(&pkts) {
                return None;
            }
            self.map.insert(
                f.sa,
                Transfer {
                    pgn: pgn & 0x3FFFF,
                    total: size,
                    packets: pkts,
                    buf: vec![0; size],
                    seen: vec![false; pkts],
                    got: 0,
                    touched_ms: now_ms,
                },
            );
            return None;
        }

        if f.pgn == 0xEB00 && f.data.len() >= 2 {
            let b = self.map.get_mut(&f.sa)?;
            let seq = usize::from(f.data[0]);
            if seq < 1 || seq > b.packets {
                return None;
            }
            // A repeated sequence number must not count toward completion, or a retransmit makes a
            // partly-filled buffer look finished and we hand up a truncated DM1.
            if b.seen[seq - 1] {
                return None;
            }
            let off = (seq - 1) * 7;
            if off >= b.total {
                return None;
            }
            let n = (f.data.len() - 1).min(b.total - off);
            b.buf[off..off + n].copy_from_slice(&f.data[1..1 + n]);
            b.seen[seq - 1] = true;
            b.got += 1;
            b.touched_ms = now_ms;
            if b.got < b.packets {
                return None;
            }
            let done = self.map.remove(&f.sa).expect("transfer present");
            return Some(J1939Frame {
                pgn: done.pgn,
                sa: f.sa,
                da: f.da,
                data: done.buf,
            });
        }

        Some(f.clone())
    }

    fn drop_stale(&mut self, now_ms: u64) {
        self.map
            .retain(|_, t| now_ms.saturating_sub(t.touched_ms) <= STALE_MS);
    }
}
