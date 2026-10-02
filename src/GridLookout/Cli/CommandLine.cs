using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GridLookout.Cli;

/// <summary>
/// The command-line surface of GridLookout.exe in one place: the switch table that --help prints
/// (kept next to the recogniser so the two cannot drift), the help-request test, and the
/// unrecognised-switch scan Program.cs warns about at startup. Pure and static - unit-tested in
/// GridLookout.Tests/Cli. Program.cs still does the actual dispatch; this class only knows names.
/// </summary>
public static class CommandLine
{
    public sealed class Switch
    {
        public Switch(string name, bool takesValue, string valueName, string help)
        {
            Name = name; TakesValue = takesValue; ValueName = valueName; Help = help;
        }

        public string Name { get; }
        public bool TakesValue { get; }
        public string ValueName { get; }
        public string Help { get; }
    }

    /// <summary>
    /// Every switch Program.cs acts on. Adding a switch there without adding it here makes the
    /// startup warning fire on it - CommandLineTests pins this list, so the test suite catches it.
    /// </summary>
    public static readonly IReadOnlyList<Switch> KnownSwitches = new[]
    {
        new Switch("--recorder", true, "<name>",
            "Match this recorder by exact name for this run only (overrides RecorderNameOverride; not written back)."),
        new Switch("--monitor", true, "<n>",
            "Treat monitor <n> as the default/first monitor - the one the bare $layout{} token addresses."),
        new Switch("--protect-password", false, "",
            "Migrate a plaintext Password in camerawall.json to the DPAPI PasswordProtected blob, then exit."),
        new Switch("--health-probe", false, "",
            "Check an already-running wall via health.json; exit 0 healthy, 1 degraded, 2 hung, 3 absent."),
        new Switch("--screenshot", false, "",
            "Ask the running wall to save screen-<n>.png for every display, print the paths, exit."),
        new Switch("--export-camera-bindings", false, "",
            "Log in, print and write a CameraBindings skeleton (camera-bindings.generated.json), exit."),
        new Switch("--license", false, "",
            "Print licence status, edition, seats, term, dates, licence file path, and machine fingerprint; exit 0 if Licensed/Trial, 3 otherwise."),
        new Switch("--license-install", true, "<path>",
            "Verify a licence file and install it to the state directory as gridlookout.lic; exit 0 on success, 1 if invalid."),
        new Switch("--license-fingerprint", false, "",
            "Print only this machine's licence fingerprint."),
        new Switch("--help", false, "",
            "Print this table and exit (also -h, -?, /?, /help)."),
    };

    private static readonly string[] HelpForms = { "--help", "-h", "-?", "/?", "/help", "/h" };

    public static bool IsHelpRequested(string[] args) =>
        args.Any(a => HelpForms.Contains(a, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Tokens that look like switches (leading "--" or "/") but match nothing in
    /// <see cref="KnownSwitches"/>. The value following a value-taking switch is skipped, so
    /// `--recorder /odd-name` never flags the recorder name.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownSwitches(string[] args)
    {
        var unknown = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            var known = KnownSwitches.FirstOrDefault(s => string.Equals(s.Name, a, StringComparison.OrdinalIgnoreCase));
            if (known is not null)
            {
                if (known.TakesValue) i++;
                continue;
            }
            if (HelpForms.Contains(a, StringComparer.OrdinalIgnoreCase)) continue;
            if (a.StartsWith("--", StringComparison.Ordinal) || a.StartsWith("/", StringComparison.Ordinal))
            {
                unknown.Add(a);
            }
        }
        return unknown;
    }

    public static string BuildUsage(string version)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"GridLookout {version} - Milestone XProtect video wall (IT42 d.o.o.)");
        sb.AppendLine();
        sb.AppendLine("Usage: GridLookout.exe [switch] ...      (no switch = start the wall)");
        sb.AppendLine();
        int width = KnownSwitches.Max(s => (s.Name + " " + s.ValueName).TrimEnd().Length) + 2;
        foreach (var s in KnownSwitches)
        {
            string left = (s.Name + " " + s.ValueName).TrimEnd();
            sb.AppendLine("  " + left.PadRight(width) + s.Help);
        }
        sb.AppendLine();
        sb.AppendLine("Configuration: camerawall.json next to the exe (or %ProgramData%\\GridLookout when the exe folder is read-only).");
        sb.AppendLine("Manual: camerawall-admin-guide.md in the docs folder next to the exe, section \"Command-line arguments\".");
        return sb.ToString();
    }
}
