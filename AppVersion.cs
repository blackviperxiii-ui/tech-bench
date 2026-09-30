using System;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("Tech Bench")]
[assembly: AssemblyProduct("Tech Bench")]
[assembly: AssemblyDescription("Shop tool for knowledge-base search and INLINE 7 / J1939")]
[assembly: AssemblyVersion("1.2.8.0")]
[assembly: AssemblyFileVersion("1.2.8.0")]
[assembly: AssemblyInformationalVersion("1.2.8")]

namespace TechBench
{
    /// <summary>
    /// Single stamped version. Keep the assembly attributes above in lockstep with Number.
    /// release.bat reads Number when it writes latest.json and the Setup exe filename.
    /// </summary>
    internal static class AppVersion
    {
        public const string Number = "1.2.8";
    }

    internal sealed class SmokeResult
    {
        public int ExitCode;
        public string Line;
        public int Records;
    }

    /// <summary>
    /// Headless smoke check shared by TechBench.exe and SelfTest.
    /// Does not write settings, show a window, or open the bus.
    /// </summary>
    internal static class InstallSmoke
    {
        public static SmokeResult Run()
        {
            return Run(null);
        }

        /// <summary>
        /// A null or blank kbRoot loads KbIndex.FindRoot(). Tests pass an explicit folder.
        /// </summary>
        public static SmokeResult Run(string kbRoot)
        {
            var result = new SmokeResult();
            if (string.IsNullOrWhiteSpace(AppVersion.Number))
            {
                result.ExitCode = 1;
                result.Line = "smoke FAIL: version is empty";
                return result;
            }

            string root = kbRoot;
            if (string.IsNullOrWhiteSpace(root))
                root = KbIndex.FindRoot();

            var kb = new KbIndex();
            try
            {
                kb.Load(root);
            }
            catch (Exception ex)
            {
                result.ExitCode = 1;
                result.Records = 0;
                result.Line = "smoke FAIL: " + (ex == null || ex.Message == null ? "knowledge base did not load" : ex.Message);
                return result;
            }

            int n = kb.All.Count;
            result.Records = n;
            if (n <= 0)
            {
                result.ExitCode = 1;
                result.Line = "smoke FAIL: no records loaded from " + root;
                return result;
            }

            result.ExitCode = 0;
            result.Line = "TechBench " + AppVersion.Number + " smoke OK kb=" + root + " records=" + n;
            return result;
        }

        public static void Emit(SmokeResult result)
        {
            EmitLine(result == null ? "smoke FAIL: no result" : result.Line);
        }

        public static void EmitLine(string line)
        {
            try { Console.Out.WriteLine(line ?? ""); }
            catch { }
        }

        public static bool TryWriteReport(string path, string line)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    EmitLine("smoke FAIL: report path is empty");
                    return false;
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, (line ?? "") + Environment.NewLine);
                return true;
            }
            catch (Exception ex)
            {
                EmitLine("smoke FAIL: could not write " + path + ": " + ex.Message);
                return false;
            }
        }
    }
}
