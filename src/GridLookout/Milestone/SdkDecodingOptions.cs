using System.Text.RegularExpressions;

namespace GridLookout.Milestone;

/// <summary>
/// Pure normalisation of the two MIP SDK media-toolkit knobs GridLookout sets before login
/// (<c>WallConfig.HardwareDecoding</c>, <c>WallConfig.SdkDecodingThreads</c>). Kept SDK-free so it
/// is unit-testable; <see cref="MilestoneSession.Initialize"/> applies the results.
/// </summary>
public static class SdkDecodingOptions
{
    public const string DefaultHardwareDecoding = "Off";
    public const string DefaultDecodingThreads = "1,1";

    private static readonly Regex ThreadsPattern = new(@"^\s*([1-9]\d{0,2})\s*,\s*([1-9]\d{0,2})\s*$", RegexOptions.Compiled);

    /// <summary>Returns "Off" or "Auto". Anything else yields "Off" and a warning line via
    /// <paramref name="warn"/> (null-safe).</summary>
    public static string NormalizeHardwareDecoding(string? value, Action<string>? warn = null)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return "Auto";
        if (v.Equals("Off", StringComparison.OrdinalIgnoreCase) || v.Length == 0) return "Off";
        warn?.Invoke($"HardwareDecoding '{value}' is not 'Off' or 'Auto' - using Off.");
        return "Off";
    }

    /// <summary>Returns a canonical "n,m" string (two positive integers, max 999) or the default with
    /// a warning line via <paramref name="warn"/>.</summary>
    public static string NormalizeDecodingThreads(string? value, Action<string>? warn = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultDecodingThreads;
        var m = ThreadsPattern.Match(value);
        if (m.Success) return $"{m.Groups[1].Value},{m.Groups[2].Value}";
        warn?.Invoke($"SdkDecodingThreads '{value}' is not '<n>,<m>' (two positive integers) - using {DefaultDecodingThreads}.");
        return DefaultDecodingThreads;
    }
}
