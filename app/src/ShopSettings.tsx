import { useEffect, useState } from "react";
import {
  pickAssignedFile,
  pickFolder,
  shopSettingsGet,
  shopSettingsSave,
  updateCheck,
  updateInstall,
  type UpdateInfo,
} from "./api";

/** Port of ShopSettingsForm.cs: tech name, shop share folder, optional assigned WO file. */
export function ShopSettings({ onSaved }: { onSaved: () => void }) {
  const [techId, setTechId] = useState("");
  const [share, setShare] = useState("");
  const [assigned, setAssigned] = useState("");
  const [status, setStatus] = useState("");
  const [update, setUpdate] = useState<UpdateInfo | null>(null);
  const [updateText, setUpdateText] = useState("");
  const [updating, setUpdating] = useState(false);

  useEffect(() => {
    shopSettingsGet()
      .then((s) => {
        setTechId(s.techId);
        setShare(s.shareFolder);
        setAssigned(s.assignedFile);
        setStatus(`Saved in ${s.path}`);
      })
      .catch((e) => setStatus(String(e)));
  }, []);

  async function save() {
    try {
      const s = await shopSettingsSave(techId, share, assigned);
      setTechId(s.techId);
      setShare(s.shareFolder);
      setAssigned(s.assignedFile);
      setStatus(`Saved shop settings to ${s.path}`);
      onSaved();
    } catch (e) {
      setStatus(`Could not save: ${String(e)}`);
    }
  }

  async function check() {
    setUpdating(true);
    setUpdateText("Checking…");
    try {
      const u = await updateCheck();
      setUpdate(u);
      setUpdateText(
        u.available
          ? `Version ${u.available} is available (this is ${u.current}).${u.notes ? `\n${u.notes}` : ""}`
          : `Tech Bench ${u.current} is the newest version.`,
      );
    } catch (e) {
      setUpdate(null);
      setUpdateText(String(e));
    } finally {
      setUpdating(false);
    }
  }

  async function install() {
    setUpdating(true);
    setUpdateText(`Downloading ${update?.available ?? "the update"}… Tech Bench restarts when it is installed.`);
    try {
      await updateInstall();
    } catch (e) {
      setUpdateText(String(e));
      setUpdating(false);
    }
  }

  async function browse(pick: () => Promise<string | null>, set: (v: string) => void) {
    try {
      const p = await pick();
      if (p) set(p);
    } catch (e) {
      setStatus(String(e));
    }
  }

  return (
    <section className="settings">
      <h2>Shop settings</h2>
      <p className="muted">
        Work orders are saved on this PC and in the knowledge base shop folder. Add a share folder to also
        copy shared work orders there for other techs.
      </p>
      <div className="wo-form">
        <label className="wide">
          Tech name
          <input
            value={techId}
            title="Your user name by default; also used as the shop-folder shard"
            onChange={(e) => setTechId(e.target.value)}
          />
        </label>
        <label className="wide">
          Shop share folder
          <span className="row">
            <input
              value={share}
              title="USB / network / extra OneDrive folder other techs use"
              onChange={(e) => setShare(e.target.value)}
            />
            <button onClick={() => void browse(pickFolder, setShare)}>Browse…</button>
          </span>
        </label>
        <label className="wide">
          Assigned WO file
          <span className="row">
            <input
              value={assigned}
              title="Optional id-work-orders.json or .csv"
              onChange={(e) => setAssigned(e.target.value)}
            />
            <button onClick={() => void browse(pickAssignedFile, setAssigned)}>Browse…</button>
          </span>
        </label>
      </div>
      <div className="bar settings-foot">
        <button onClick={() => void save()}>Save</button>
      </div>
      <div className="status" role="status">
        {status}
      </div>

      <h2>Updates</h2>
      <div className="bar">
        <button disabled={updating} onClick={() => void check()}>
          Check for updates
        </button>
        {update?.available && (
          <button className="primary" disabled={updating} onClick={() => void install()}>
            Install {update.available}
          </button>
        )}
      </div>
      <pre className="detail mini" aria-label="Update status">
        {updateText || "Updates are signed; Tech Bench checks the signature before installing."}
      </pre>
    </section>
  );
}
