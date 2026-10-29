using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TechBench
{
    /// <summary>Shell-execute open for shop files (PDF, photos) with a program/script blocklist.</summary>
    internal static class SafeOpen
    {
        static readonly string[] BlockedExtensions = new string[]
        {
            ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".hta", ".cpl", ".reg", ".lnk", ".jar",
            ".appref-ms", ".application", ".gadget", ".msc", ".psc1", ".scf",
            ".url", ".vb", ".ws", ".inf", ".dll", ".sys",
            ".chm", ".docm", ".xlsm", ".pptm", ".dotm", ".xltm", ".ppsm"
        };

        /// <summary>True when this path must not be passed to shell execute. Null and empty are not launched.</summary>
        internal static bool IsBlockedLaunchPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            string trimmed = path.TrimEnd(' ', '.');
            if (trimmed.Length == 0) return true;
            string file;
            string stream;
            SplitStream(trimmed, out file, out stream);
            if (IsBlockedExtension(ExtensionOf(file))) return true;
            if (stream.Length > 0 && IsBlockedExtension(ExtensionOf(stream))) return true;
            return false;
        }

        internal static string BlockedLaunchMessage(string path)
        {
            string shown = "(no file)";
            if (!string.IsNullOrWhiteSpace(path))
            {
                string trimmed = path.Trim().TrimEnd(' ', '.');
                string name = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(name)) name = path.Trim();
                if (!string.IsNullOrEmpty(name)) shown = name;
            }
            return "Tech Bench won't open program or script files from the knowledge base: "
                + shown + ". Open it from Explorer if you trust it.";
        }

        /// <summary>Opens a document with the Windows default app via shell execute.</summary>
        public static bool TryOpen(string path, out string error)
        {
            error = null;
            if (IsBlockedLaunchPath(path))
            {
                error = BlockedLaunchMessage(path);
                return false;
            }
            if (!PathExists(path))
            {
                error = MissingFileMessage(path);
                return false;
            }
            if (!HasExecutableAssociation(path))
            {
                error = NoAssociationMessage(path);
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex)
            {
                error = ShellErrorMessage(path, ex);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = AccessDeniedMessage(path);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string MissingFileMessage(string path)
        {
            string shown = string.IsNullOrWhiteSpace(path) ? "(no file)" : path.Trim();
            return "Cannot find this file:\n" + shown
                + "\n\nCheck that the USB drive or network share is connected.";
        }

        static string AccessDeniedMessage(string path)
        {
            string shown = string.IsNullOrWhiteSpace(path) ? "(no file)" : path.Trim();
            return "Access denied opening:\n" + shown;
        }

        static string NoAssociationMessage(string path)
        {
            string trimmed = string.IsNullOrWhiteSpace(path) ? "" : path.Trim().TrimEnd(' ', '.');
            string name = trimmed.Length == 0 ? "(no file)" : Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = trimmed;
            return "No program is associated with this file type: " + name
                + ". Open it from Explorer and choose an app.";
        }

        static string ShellErrorMessage(string path, Win32Exception ex)
        {
            int code = ex.NativeErrorCode;
            if (code == 2 || code == 3 || code == 1157)
                return MissingFileMessage(path);
            if (code == 5)
                return AccessDeniedMessage(path);
            if (code == 31 || code == 42 || code == 1155)
                return NoAssociationMessage(path);
            string msg = ex.Message;
            if (msg != null)
            {
                if (msg.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AccessDeniedMessage(path);
                if (msg.IndexOf("No application is associated", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("no application is associated", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("associated with the specified file", StringComparison.OrdinalIgnoreCase) >= 0)
                    return NoAssociationMessage(path);
            }
            return msg ?? "Could not open the file.";
        }

        static bool PathExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        const int AssocStrExecutable = 2;
        // ASSOCF_INIT_IGNOREUNKNOWN: an unregistered extension fails instead of answering with the
        // "Open with" picker, so a size answer means a real app.
        const int AssocfInitIgnoreUnknown = 0x400;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int AssocQueryString(
            int flags,
            int str,
            string pszAssoc,
            string pszExtra,
            StringBuilder pszOut,
            ref uint pcchOut);

        internal static bool HasExecutableAssociation(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.TrimEnd(' ', '.');
            if (ext.Length == 0 || ext[0] != '.') return false;
            uint length;
            // A size query (pszOut null) is documented to answer S_FALSE (1) with the length, so 1
            // is success here, not "no association".
            int hr = ExecutableSizeQuery(ext, out length);
            if ((hr != 0 && hr != 1) || length == 0) return false;
            var sb = new StringBuilder((int)length + 1);
            hr = AssocQueryString(AssocfInitIgnoreUnknown, AssocStrExecutable, ext, null, sb, ref length);
            return hr == 0 && sb.Length > 0;
        }

        /// <summary>Raw HRESULT of the size query, so SelfTest can log what this Windows answers.</summary>
        internal static int ExecutableSizeQuery(string ext, out uint length)
        {
            length = 0;
            return AssocQueryString(AssocfInitIgnoreUnknown, AssocStrExecutable, ext, null, null, ref length);
        }

        static void SplitStream(string path, out string file, out string stream)
        {
            file = path;
            stream = "";
            int start = 0;
            if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':') start = 2;
            int colon = path.IndexOf(':', start);
            if (colon < 0) return;
            file = path.Substring(0, colon);
            stream = path.Substring(colon + 1);
        }

        static string ExtensionOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOfAny(new char[] { '\\', '/' });
            int colon = path.LastIndexOf(':');
            int start = slash + 1;
            int end = path.Length;
            if (colon > slash && !IsDriveColon(path, colon)) end = colon;
            if (end < start) return "";
            string name = path.Substring(start, end - start).TrimEnd(' ', '.');
            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot >= name.Length - 1) return "";
            return name.Substring(dot);
        }

        static bool IsDriveColon(string path, int colon)
        {
            return colon == 1 && path.Length > 1 && char.IsLetter(path[0]);
        }

        static bool IsBlockedExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            for (int i = 0; i < BlockedExtensions.Length; i++)
            {
                if (string.Equals(ext, BlockedExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
