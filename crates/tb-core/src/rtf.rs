//! Minimal RTF reader for the shipped cheat sheet (`assets/cheatsheet.rtf`), so the new UI shows
//! the same text the WinForms RichTextBox loads. Handles paragraphs, tabs, bold, font size and
//! escapes; skips the font/colour/style tables and `\*` destinations. Not a general RTF parser.

/// One `\par`-terminated paragraph. Tabs are kept as `'\t'`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Para {
    pub text: String,
    /// Every visible character is bold (a section heading in the cheat sheet).
    pub bold: bool,
    /// Some text has an explicit font size above 10 pt (`\fs` > 20 half-points).
    pub big: bool,
}

const SKIP_DESTINATIONS: &[&str] = &[
    "fonttbl",
    "colortbl",
    "stylesheet",
    "info",
    "pict",
    "header",
    "footer",
];

#[derive(Clone, Copy)]
struct State {
    bold: bool,
    fs: i32,
    skip: bool,
}

struct Builder {
    paras: Vec<Para>,
    text: String,
    all_bold: bool,
    any_visible: bool,
    big: bool,
}

impl Builder {
    fn push(&mut self, c: char, st: State) {
        if st.skip {
            return;
        }
        self.text.push(c);
        if !c.is_whitespace() {
            self.any_visible = true;
            self.all_bold &= st.bold;
            self.big |= st.fs > 20;
        }
    }

    fn end_para(&mut self) {
        self.paras.push(Para {
            text: std::mem::take(&mut self.text),
            bold: self.any_visible && self.all_bold,
            big: self.big,
        });
        self.all_bold = true;
        self.any_visible = false;
        self.big = false;
    }
}

/// Windows-1252 bytes 0x80..=0x9F that differ from Latin-1.
fn cp1252(b: u8) -> char {
    match b {
        0x80 => '€',
        0x91 => '‘',
        0x92 => '’',
        0x93 => '“',
        0x94 => '”',
        0x95 => '•',
        0x96 => '–',
        0x97 => '—',
        0x85 => '…',
        _ => b as char,
    }
}

/// Reads `rtf` into paragraphs. Text after the last `\par` is kept as a final paragraph.
pub fn paragraphs(rtf: &str) -> Vec<Para> {
    let chars: Vec<char> = rtf.chars().collect();
    let mut out = Builder {
        paras: Vec::new(),
        text: String::new(),
        all_bold: true,
        any_visible: false,
        big: false,
    };
    let mut st = State {
        bold: false,
        // 0 = no \fs seen; only an explicit size above 10 pt counts as big.
        fs: 0,
        skip: false,
    };
    let mut stack: Vec<State> = Vec::new();
    // True right after `{`: the next control word decides whether the group is a destination.
    let mut group_start = false;
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        match c {
            '{' => {
                stack.push(st);
                group_start = true;
                i += 1;
                continue;
            }
            '}' => {
                st = stack.pop().unwrap_or(st);
                i += 1;
            }
            '\\' => {
                i += 1;
                let Some(&n) = chars.get(i) else { break };
                if n.is_ascii_alphabetic() {
                    let start = i;
                    while i < chars.len() && chars[i].is_ascii_alphabetic() {
                        i += 1;
                    }
                    let word: String = chars[start..i].iter().collect();
                    let num_start = i;
                    if i < chars.len() && chars[i] == '-' {
                        i += 1;
                    }
                    while i < chars.len() && chars[i].is_ascii_digit() {
                        i += 1;
                    }
                    let num: Option<i32> = if i > num_start {
                        chars[num_start..i].iter().collect::<String>().parse().ok()
                    } else {
                        None
                    };
                    if i < chars.len() && chars[i] == ' ' {
                        i += 1;
                    }
                    if group_start && SKIP_DESTINATIONS.contains(&word.as_str()) {
                        st.skip = true;
                    }
                    match word.as_str() {
                        "par" => {
                            if !st.skip {
                                out.end_para();
                            }
                        }
                        "line" => out.push('\n', st),
                        "tab" => out.push('\t', st),
                        "b" => st.bold = num != Some(0),
                        "plain" => {
                            st.bold = false;
                            st.fs = 0;
                        }
                        "fs" => st.fs = num.unwrap_or(0),
                        _ => {}
                    }
                } else {
                    i += 1;
                    match n {
                        '*' if group_start => st.skip = true,
                        '\\' | '{' | '}' => out.push(n, st),
                        '~' => out.push('\u{a0}', st),
                        '\'' => {
                            let hex: String = chars.iter().skip(i).take(2).collect();
                            if let Ok(b) = u8::from_str_radix(&hex, 16) {
                                out.push(cp1252(b), st);
                            }
                            i += 2;
                        }
                        _ => {}
                    }
                }
            }
            '\r' | '\n' => i += 1,
            _ => {
                out.push(c, st);
                i += 1;
            }
        }
        group_start = false;
    }
    if out.any_visible {
        out.end_para();
    }
    out.paras
}
