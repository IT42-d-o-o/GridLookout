using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GridLookout.Licensing;

/// <summary>
/// Derives a stable, deterministic, per-machine identifier that works fully offline - the SAME
/// format InfraAtlas's MachineFingerprint uses (SHA-256 of the Windows MachineGuid, first 16 bytes as
/// "XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX"), so one signing tool can generate node-locked licences for
/// both products later (see docs/plans/2026-09-25-gridlookout-licensing.md). Ported, not referenced -
/// GridLookout is net48, InfraAtlas is net9. Windows-only here (unlike InfraAtlas's cross-platform
/// version): GridLookout is a WinForms kiosk app (see GridLookout.csproj's TargetFramework comment)
/// and never runs anywhere a Linux machine-id path would matter.
///
/// Never throws: on any failure a clearly-marked fallback is returned so the app can continue running
/// (node-lock just will not match, and the trial marker's anti-copy HMAC binds to whatever fallback
/// value this machine consistently produces instead).
/// </summary>
public static class MachineFingerprint
{
    private const string FallbackPrefix = "FALLBACK-";

    public static string Compute()
    {
        try
        {
            var rawId = GetWindowsMachineGuid();
            if (string.IsNullOrWhiteSpace(rawId))
            {
                return BuildFallback();
            }

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId.Trim()));
            var hex = ToHex(hashBytes, 0, 16);
            return $"{hex.Substring(0, 8)}-{hex.Substring(8, 8)}-{hex.Substring(16, 8)}-{hex.Substring(24, 8)}";
        }
        catch
        {
            return BuildFallback();
        }
    }

    private static string GetWindowsMachineGuid()
    {
#pragma warning disable CA1416
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
#pragma warning restore CA1416
    }

    private static string BuildFallback()
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName ?? string.Empty));
        return FallbackPrefix + ToHex(hashBytes, 0, 14);
    }

    /// <summary>Uppercase hex of <paramref name="count"/> bytes starting at <paramref name="offset"/>
    /// - net48 has no <c>Convert.ToHexString</c> (added .NET 5+), so this is a plain manual loop.</summary>
    private static string ToHex(byte[] bytes, int offset, int count)
    {
        var sb = new StringBuilder(count * 2);
        for (int i = 0; i < count; i++)
        {
            sb.Append(bytes[offset + i].ToString("X2"));
        }

        return sb.ToString();
    }
}
