//! Small std-only JSON reader for the knowledge-base files.
//!
//! The C# app reads these with `JavaScriptSerializer` and then turns every scalar back into text
//! with `Convert.ToString`. [`Value::text`] gives the same strings: numbers keep their source
//! spelling, booleans print as `True`/`False`, null is empty.

use std::fmt;

#[derive(Debug, Clone, PartialEq)]
pub enum Value {
    Null,
    Bool(bool),
    /// The number exactly as written in the file.
    Number(String),
    String(String),
    Array(Vec<Value>),
    /// Keys in file order, as `JavaScriptSerializer` keeps them.
    Object(Vec<(String, Value)>),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParseError {
    pub offset: usize,
    pub message: &'static str,
}

impl fmt::Display for ParseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "invalid JSON at byte {}: {}", self.offset, self.message)
    }
}

impl std::error::Error for ParseError {}

impl Value {
    /// Member of an object; `None` for a missing key or a non-object.
    pub fn get(&self, key: &str) -> Option<&Value> {
        match self {
            Value::Object(m) => m.iter().find(|(k, _)| k == key).map(|(_, v)| v),
            _ => None,
        }
    }

    /// Items of an array; empty for anything else (the C# `Arr` helper).
    pub fn items(&self) -> &[Value] {
        match self {
            Value::Array(a) => a,
            _ => &[],
        }
    }

    pub fn is_object(&self) -> bool {
        matches!(self, Value::Object(_))
    }

    /// `Convert.ToString` of the deserialized value (the C# `S` helper).
    pub fn text(&self) -> String {
        match self {
            Value::Null => String::new(),
            Value::Bool(true) => "True".to_string(),
            Value::Bool(false) => "False".to_string(),
            Value::Number(n) => n.clone(),
            Value::String(s) => s.clone(),
            Value::Array(_) => "System.Collections.ArrayList".to_string(),
            Value::Object(_) => {
                "System.Collections.Generic.Dictionary`2[System.String,System.Object]".to_string()
            }
        }
    }
}

impl Value {
    /// Compact JSON text. Object members keep their order.
    pub fn to_json(&self) -> String {
        let mut out = String::new();
        write_value(self, &mut out);
        out
    }
}

fn write_value(v: &Value, out: &mut String) {
    match v {
        Value::Null => out.push_str("null"),
        Value::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
        Value::Number(n) => out.push_str(n),
        Value::String(s) => write_string(s, out),
        Value::Array(items) => {
            out.push('[');
            for (i, x) in items.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                write_value(x, out);
            }
            out.push(']');
        }
        Value::Object(members) => {
            out.push('{');
            for (i, (k, x)) in members.iter().enumerate() {
                if i > 0 {
                    out.push(',');
                }
                write_string(k, out);
                out.push(':');
                write_value(x, out);
            }
            out.push('}');
        }
    }
}

fn write_string(s: &str, out: &mut String) {
    out.push('"');
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            // Like JavaScriptSerializer: control characters and HTML-sensitive characters as \uXXXX.
            c if (c as u32) < 0x20 || matches!(c, '<' | '>' | '&' | '\'') => {
                out.push_str(&format!("\\u{:04x}", c as u32));
            }
            c => out.push(c),
        }
    }
    out.push('"');
}

/// `S(Get(d, key))`: text of a member, empty when missing.
pub fn s(v: &Value, key: &str) -> String {
    v.get(key).map(Value::text).unwrap_or_default()
}

pub fn parse(text: &str) -> Result<Value, ParseError> {
    let mut p = Parser {
        b: text.as_bytes(),
        i: 0,
        src: text,
    };
    // A UTF-8 byte order mark is common in files saved by Windows tools.
    if p.b.starts_with(&[0xEF, 0xBB, 0xBF]) {
        p.i = 3;
    }
    p.ws();
    let v = p.value(0)?;
    p.ws();
    if p.i != p.b.len() {
        return Err(p.err("trailing characters"));
    }
    Ok(v)
}

const MAX_DEPTH: usize = 128;

struct Parser<'a> {
    b: &'a [u8],
    i: usize,
    src: &'a str,
}

impl Parser<'_> {
    fn err(&self, message: &'static str) -> ParseError {
        ParseError {
            offset: self.i,
            message,
        }
    }

    fn ws(&mut self) {
        while self.i < self.b.len() && matches!(self.b[self.i], b' ' | b'\t' | b'\r' | b'\n') {
            self.i += 1;
        }
    }

    fn eat(&mut self, lit: &str) -> bool {
        if self.b[self.i..].starts_with(lit.as_bytes()) {
            self.i += lit.len();
            true
        } else {
            false
        }
    }

    fn value(&mut self, depth: usize) -> Result<Value, ParseError> {
        if depth > MAX_DEPTH {
            return Err(self.err("nested too deep"));
        }
        match self.b.get(self.i) {
            None => Err(self.err("unexpected end")),
            Some(b'{') => self.object(depth),
            Some(b'[') => self.array(depth),
            Some(b'"') => Ok(Value::String(self.string()?)),
            Some(b't') if self.eat("true") => Ok(Value::Bool(true)),
            Some(b'f') if self.eat("false") => Ok(Value::Bool(false)),
            Some(b'n') if self.eat("null") => Ok(Value::Null),
            Some(c) if *c == b'-' || c.is_ascii_digit() => self.number(),
            Some(_) => Err(self.err("unexpected character")),
        }
    }

    fn object(&mut self, depth: usize) -> Result<Value, ParseError> {
        self.i += 1;
        let mut members: Vec<(String, Value)> = Vec::new();
        self.ws();
        if self.b.get(self.i) == Some(&b'}') {
            self.i += 1;
            return Ok(Value::Object(members));
        }
        loop {
            self.ws();
            if self.b.get(self.i) != Some(&b'"') {
                return Err(self.err("expected a key"));
            }
            let k = self.string()?;
            self.ws();
            if self.b.get(self.i) != Some(&b':') {
                return Err(self.err("expected ':'"));
            }
            self.i += 1;
            self.ws();
            let v = self.value(depth + 1)?;
            // Last duplicate wins, as with a dictionary.
            match members.iter_mut().find(|(key, _)| *key == k) {
                Some(slot) => slot.1 = v,
                None => members.push((k, v)),
            }
            self.ws();
            match self.b.get(self.i) {
                Some(b',') => self.i += 1,
                Some(b'}') => {
                    self.i += 1;
                    return Ok(Value::Object(members));
                }
                _ => return Err(self.err("expected ',' or '}'")),
            }
        }
    }

    fn array(&mut self, depth: usize) -> Result<Value, ParseError> {
        self.i += 1;
        let mut items = Vec::new();
        self.ws();
        if self.b.get(self.i) == Some(&b']') {
            self.i += 1;
            return Ok(Value::Array(items));
        }
        loop {
            self.ws();
            items.push(self.value(depth + 1)?);
            self.ws();
            match self.b.get(self.i) {
                Some(b',') => self.i += 1,
                Some(b']') => {
                    self.i += 1;
                    return Ok(Value::Array(items));
                }
                _ => return Err(self.err("expected ',' or ']'")),
            }
        }
    }

    fn number(&mut self) -> Result<Value, ParseError> {
        let start = self.i;
        if self.b[self.i] == b'-' {
            self.i += 1;
        }
        let digits = |p: &mut Self| {
            let s = p.i;
            while p.i < p.b.len() && p.b[p.i].is_ascii_digit() {
                p.i += 1;
            }
            p.i > s
        };
        if !digits(self) {
            return Err(self.err("bad number"));
        }
        if self.b.get(self.i) == Some(&b'.') {
            self.i += 1;
            if !digits(self) {
                return Err(self.err("bad number"));
            }
        }
        if matches!(self.b.get(self.i), Some(b'e' | b'E')) {
            self.i += 1;
            if matches!(self.b.get(self.i), Some(b'+' | b'-')) {
                self.i += 1;
            }
            if !digits(self) {
                return Err(self.err("bad number"));
            }
        }
        Ok(Value::Number(self.src[start..self.i].to_string()))
    }

    fn hex4(&mut self) -> Result<u32, ParseError> {
        let h = self
            .b
            .get(self.i..self.i + 4)
            .ok_or_else(|| self.err("short \\u escape"))?;
        let s = std::str::from_utf8(h).map_err(|_| self.err("bad \\u escape"))?;
        let v = u32::from_str_radix(s, 16).map_err(|_| self.err("bad \\u escape"))?;
        self.i += 4;
        Ok(v)
    }

    fn string(&mut self) -> Result<String, ParseError> {
        self.i += 1;
        let mut out = String::new();
        loop {
            let start = self.i;
            while self.i < self.b.len() && self.b[self.i] != b'"' && self.b[self.i] != b'\\' {
                self.i += 1;
            }
            out.push_str(&self.src[start..self.i]);
            match self.b.get(self.i) {
                None => return Err(self.err("unterminated string")),
                Some(b'"') => {
                    self.i += 1;
                    return Ok(out);
                }
                _ => {
                    self.i += 1;
                    let c = *self
                        .b
                        .get(self.i)
                        .ok_or_else(|| self.err("unterminated string"))?;
                    self.i += 1;
                    match c {
                        b'"' => out.push('"'),
                        b'\\' => out.push('\\'),
                        b'/' => out.push('/'),
                        b'b' => out.push('\u{8}'),
                        b'f' => out.push('\u{c}'),
                        b'n' => out.push('\n'),
                        b'r' => out.push('\r'),
                        b't' => out.push('\t'),
                        b'u' => {
                            let hi = self.hex4()?;
                            let cp = if (0xD800..0xDC00).contains(&hi) && self.eat("\\u") {
                                let lo = self.hex4()?;
                                if (0xDC00..0xE000).contains(&lo) {
                                    0x10000 + ((hi - 0xD800) << 10) + (lo - 0xDC00)
                                } else {
                                    0xFFFD
                                }
                            } else {
                                hi
                            };
                            out.push(char::from_u32(cp).unwrap_or('\u{FFFD}'));
                        }
                        _ => return Err(self.err("bad escape")),
                    }
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_nested_values() {
        let v = parse(r#"{"a":[1,-2.50,"x\né😀"],"b":{"c":true,"d":null},"a2":false}"#).unwrap();
        assert_eq!(v.get("a").unwrap().items().len(), 3);
        assert_eq!(v.get("a").unwrap().items()[1].text(), "-2.50");
        assert_eq!(v.get("a").unwrap().items()[2].text(), "x\né😀");
        assert_eq!(v.get("b").unwrap().get("c").unwrap().text(), "True");
        assert_eq!(s(&v, "a2"), "False");
        assert_eq!(s(v.get("b").unwrap(), "d"), "");
        assert_eq!(s(&v, "missing"), "");
    }

    #[test]
    fn rejects_broken_json() {
        assert!(parse("{ this is not json ").is_err());
        assert!(parse("{ broken").is_err());
        assert!(parse(r#"{"a":1} x"#).is_err());
        assert!(parse(r#"{"a":"unterminated}"#).is_err());
        assert!(parse(&"[".repeat(500)).is_err());
    }

    #[test]
    fn writes_and_reads_back() {
        let v = Value::Object(vec![
            ("a".into(), Value::String("q\"\\\n<&>'\u{1}é".into())),
            (
                "b".into(),
                Value::Array(vec![
                    Value::Bool(true),
                    Value::Null,
                    Value::Number("12".into()),
                ]),
            ),
        ]);
        let text = v.to_json();
        assert_eq!(
            text,
            r#"{"a":"q\"\\\n\u003c\u0026\u003e\u0027\u0001é","b":[true,null,12]}"#
        );
        assert_eq!(parse(&text).unwrap(), v);
    }

    #[test]
    fn skips_bom() {
        assert!(parse("\u{FEFF}{\"codes\":[]}").unwrap().is_object());
    }
}
