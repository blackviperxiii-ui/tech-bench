import { useCallback, useEffect, useState } from "react";
import { busCall, busCaps } from "./api";

interface Summary {
  text: string;
  count: number;
}

/**
 * Adapters page (AdaptersPage.cs): the RP1210 adapters this PC has, as the 32-bit bridge reads
 * them from the RP1210 INI files, and Rescan after installing drivers.
 */
export function Adapters() {
  const [text, setText] = useState("Reading the RP1210 adapter list…");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (cmd: "summary" | "rescan") => {
    setBusy(true);
    setError("");
    try {
      const caps = await busCaps();
      if (!caps.available) {
        setText(caps.reason);
        return;
      }
      setText((await busCall<Summary>(cmd)).text);
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void load("summary");
  }, [load]);

  return (
    <div className="adapters">
      <div className="bar">
        <button onClick={() => void load("rescan")} disabled={busy}>
          Rescan
        </button>
      </div>
      {error && (
        <div className="notice" role="alert">
          {error}
        </div>
      )}
      <pre className="detail" aria-label="Adapters">
        {text}
      </pre>
    </div>
  );
}
