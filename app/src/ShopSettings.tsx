import { useEffect, useState } from "react";
import { pickAssignedFile, pickFolder, shopSettingsGet, shopSettingsSave } from "./api";

/** Port of ShopSettingsForm.cs: tech name, shop share folder, optional assigned WO file. */
export function ShopSettings({ onSaved }: { onSaved: () => void }) {
  const [techId, setTechId] = useState("");
  const [share, setShare] = useState("");
  const [assigned, setAssigned] = useState("");
  const [status, setStatus] = useState("");

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
    </section>
  );
}
