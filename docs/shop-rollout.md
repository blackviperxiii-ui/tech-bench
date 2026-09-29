# Shop rollout

How to put Tech Bench on a shop PC, upgrade it, and roll it back. This follows the Setup script and the app, not a separate installer.

## Where to get Setup

Ask someone who can open the private repo `blackviperxiii-ui/tech-bench`. They download `TechBench-Setup-<version>.exe` from the private GitHub release `v<version>` (the build writes that file as `dist\TechBench-Setup-<version>.exe`). The same exe is in the Windows installer CI artifact.

Copy that one file to the shop PC (USB stick or the shop share). The tech does not sign in to GitHub on the shop PC.

## Install

Close Tech Bench if it is already open. Disconnect INLINE 7.

Double-click the Setup exe. It does not ask for an administrator account. It is a per-user install.

- Default folder: `%LOCALAPPDATA%\Programs\TechBench`
- Start Menu group **Tech Bench**, including **Uninstall Tech Bench**
- Optional Desktop shortcut. That task starts unticked; tick it only if you want the icon
- Needs the .NET Framework 4 runtime. Setup looks for the v4 Full `Release` registry value (present on 4.5 and later) and stops with a message if it is missing. That message asks for the 32-bit .NET Framework 4.x runtime. Install that, then run Setup again.
- Windows 7 SP1 or later
- RP1210 adapter drivers are not in this Setup. Install those from the adapter vendor (Cummins, Noregon, and so on). Those packages are separate and often need an administrator.

At the end of the wizard you can launch Tech Bench. A silent install does not launch it.

Use the Start Menu shortcut, or the Desktop shortcut if you ticked that task. Do not copy `TechBench.exe` onto the Desktop. A copied exe is a second program: the next Setup will not replace it, and it will not see the knowledge base that was installed next to the real exe. Help → About shows the path of the copy you actually opened (`This copy:`). It should sit under the install folder, not on the Desktop.

## Upgrade

Close Tech Bench and disconnect INLINE 7. Then run the newer Setup exe.

Setup reuses the folder chosen last time. It replaces `TechBench.exe`, the icon files, and the shipped knowledge-base files with the copies inside that Setup, even when the file dates look older. Before it copies, it deletes leftover updater files in that folder if they are present: `TechBench.exe.new`, `TechBench.exe.new.sha256`, `apply-update.cmd`, and `TechBench.exe.bak`.

Files the app added later are not in that copy, so they stay. See "Where your data lives" below.

Help → Check for updates has no published feed, so it will not download a new exe. Upgrade by running the newer Setup. A shop can still point the installed copy at its own feed with `update-url.txt` next to `TechBench.exe` (one URL, lines starting with `#` are comments).

## Check the version

Open Tech Bench and choose Help → About. The first line is `Tech Bench` plus the version, the same number as in the Setup file name `TechBench-Setup-<version>.exe`.

## Roll back

Run the older `TechBench-Setup-<version>.exe` the same way. This Setup does not compare versions and does not refuse a downgrade. It puts that older exe, icons, and shipped knowledge base back over the install folder. Shop notes, settings, sessions, and work orders stay. The bundled database files (fault codes, service-access rows, manual index, filter chart, equipment rows) are replaced by that older Setup's copies.

## Where your data lives

Upgrades keep these because Setup does not delete them.

Outside the install folder:

- `%LOCALAPPDATA%\TechBench\settings.json` — last model/serial and window size
- `%LOCALAPPDATA%\TechBench\id-settings.json` — IntelliDealer settings (secrets stay on this PC)
- `%LOCALAPPDATA%\TechBench\sync.json` and `sync-state-*.json` — tech name and sync folder
- `%LOCALAPPDATA%\TechBench\work-orders\` — local work-order packets
- Session files, screenshots, and `techbench-errors.log` — `Desktop\TechBench-sessions` if that folder can be created, otherwise Documents, otherwise `%LOCALAPPDATA%\TechBench-sessions`

Inside the knowledge base the app opens. A normal install uses `air-compressor-kb` next to the exe. `TECHBENCH_KB`, then each path in `kb-path.txt` next to the exe, is tried first, and only used when that folder already contains `data\kb.json`.

In that folder:

- Shipped files such as `data\kb.json` are replaced on every Setup, including a rollback
- Codes you add: `data\shop\<tech>\user-codes.json` (an older `data\user-codes.json` is still read)
- Notes: `data\shop\<tech>\notes\`
- Added files: `data\shop\<tech>\files\`
- Shared work-order copies: `data\shop\<tech>\work-orders\` and `data\shop\_shared\work-orders\`

Next to the installed exe, if you created them, these are not part of Setup's file list and are not deleted by an upgrade: `update-url.txt`, `kb-path.txt`, `sync-path.txt`, `id-api.json`, `id-work-orders.json`.

Uninstall (Start Menu → Uninstall Tech Bench, or `unins000.exe` in the install folder) removes the files Setup copied: the exe, icons, shipped knowledge-base files, and the shortcuts. It does not remove `%LOCALAPPDATA%\TechBench` or the session folder. It has no extra uninstall-delete list, so files you added under the knowledge base after install are left behind.

## INLINE 7

Close Guidanz, USB-Link 3 Explorer, and any other RP1210 program before you connect INLINE 7. If one of them is still open, the adapter stays locked. The vendor driver package is still required; Tech Bench does not install it.
