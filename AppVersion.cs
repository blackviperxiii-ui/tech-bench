using System.Reflection;

[assembly: AssemblyTitle("Tech Bench")]
[assembly: AssemblyProduct("Tech Bench")]
[assembly: AssemblyDescription("Shop tool for knowledge-base search and INLINE 7 / J1939")]
[assembly: AssemblyVersion("1.2.7.0")]
[assembly: AssemblyFileVersion("1.2.7.0")]
[assembly: AssemblyInformationalVersion("1.2.7")]

namespace TechBench
{
    /// <summary>
    /// Single stamped version. Keep the assembly attributes above in lockstep with Number.
    /// release.bat reads Number when it writes latest.json and the Setup exe filename.
    /// </summary>
    internal static class AppVersion
    {
        public const string Number = "1.2.7";
    }
}
