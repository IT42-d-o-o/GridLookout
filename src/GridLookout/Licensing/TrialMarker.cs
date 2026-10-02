using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GridLookout.Licensing;

/// <summary>The on-disk shape of &lt;stateDir&gt;\gridlookout.trial.json - see
/// <see cref="TrialMarker"/>'s own doc comment for the HMAC scheme protecting it.</summary>
public sealed class TrialMarkerFile
{
    public DateTime StartedUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    /// <summary>Base64 HMAC-SHA256 - see <see cref="TrialMarker.ComputeHmac"/>.</summary>
    public string Hmac { get; set; } = string.Empty;
}

/// <summary>
/// Reads/writes/validates the trial marker file - pure logic, no file I/O (the caller, normally
/// <see cref="LicenseState"/>, does the actual reading/writing; this type only knows the marker's
/// shape and the rules that govern it). Starts on first run without a valid licence; a copied or
/// hand-edited marker is caught by the HMAC below, and a rolled-back system clock is caught
/// separately (see <see cref="IsClockRollback"/>).
///
/// HMAC key = SHA256(machineFingerprint + "GridLookout:TrialMarker:v1") - so a marker file copied to
/// a different machine cannot be used to reset (or extend) a trial there; the salt string is a
/// literal constant, per docs/plans/2026-09-25-gridlookout-licensing.md "Trial" section. The MAC
/// covers BOTH <see cref="TrialMarkerFile.StartedUtc"/> and <see cref="TrialMarkerFile.LastSeenUtc"/>
/// (not just StartedUtc) - an attacker able to edit LastSeenUtc alone could otherwise defeat
/// <see cref="IsClockRollback"/>'s clock-tamper detection without needing to forge anything else.
/// </summary>
public static class TrialMarker
{
    public const string FileName = "gridlookout.trial.json";

    public const int TrialDays = 30;

    private const string HmacAppSalt = "GridLookout:TrialMarker:v1";

    /// <summary>If now is more than this far behind the marker's last-seen high-water mark, the
    /// system clock has been rolled back (deliberately or otherwise) - see <see cref="IsClockRollback"/>.</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ComputeHmac(string machineFingerprint, DateTime startedUtc, DateTime lastSeenUtc)
    {
        using var sha256 = SHA256.Create();
        var key = sha256.ComputeHash(Encoding.UTF8.GetBytes(machineFingerprint + HmacAppSalt));

        using var hmac = new HMACSHA256(key);
        var data = Encoding.UTF8.GetBytes($"{startedUtc:O}|{lastSeenUtc:O}");
        return Convert.ToBase64String(hmac.ComputeHash(data));
    }

    public static bool VerifyHmac(TrialMarkerFile marker, string machineFingerprint)
    {
        if (marker is null || string.IsNullOrEmpty(marker.Hmac))
        {
            return false;
        }

        var expected = ComputeHmac(machineFingerprint, marker.StartedUtc, marker.LastSeenUtc);
        return FixedTimeEquals(expected, marker.Hmac);
    }

    /// <summary>Parses <paramref name="json"/> and verifies its HMAC against
    /// <paramref name="machineFingerprint"/> in one step - null on ANY failure (malformed JSON,
    /// missing/wrong HMAC, or a marker whose HMAC was computed for a different machine's
    /// fingerprint), so the caller never needs to distinguish "unreadable" from "tampered" from
    /// "copied from elsewhere" - all three mean the same thing: this marker cannot be trusted.</summary>
    public static TrialMarkerFile? TryRead(string json, string machineFingerprint)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<TrialMarkerFile>(json, JsonOptions);
            return marker is not null && VerifyHmac(marker, machineFingerprint) ? marker : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(TrialMarkerFile marker) => JsonSerializer.Serialize(marker, JsonOptions);

    public static TrialMarkerFile CreateFresh(string machineFingerprint, DateTime nowUtc)
    {
        var marker = new TrialMarkerFile { StartedUtc = nowUtc, LastSeenUtc = nowUtc };
        marker.Hmac = ComputeHmac(machineFingerprint, marker.StartedUtc, marker.LastSeenUtc);
        return marker;
    }

    /// <summary>Advances <see cref="TrialMarkerFile.LastSeenUtc"/> to <paramref name="nowUtc"/> and
    /// recomputes the HMAC over the new value - <see cref="TrialMarkerFile.StartedUtc"/> never
    /// changes once a trial has begun.</summary>
    public static TrialMarkerFile WithLastSeen(TrialMarkerFile marker, string machineFingerprint, DateTime nowUtc)
    {
        var updated = new TrialMarkerFile { StartedUtc = marker.StartedUtc, LastSeenUtc = nowUtc };
        updated.Hmac = ComputeHmac(machineFingerprint, updated.StartedUtc, updated.LastSeenUtc);
        return updated;
    }

    /// <summary>True when the verification clock is more than <see cref="ClockSkewTolerance"/> behind
    /// the marker's own last-seen high-water mark - i.e. the system clock has moved backwards by more
    /// than ordinary timezone/NTP jitter could explain, which would otherwise let a rolled-back clock
    /// re-extend an already-elapsed trial indefinitely.</summary>
    public static bool IsClockRollback(DateTime nowUtc, DateTime lastSeenUtc) => nowUtc < lastSeenUtc - ClockSkewTolerance;

    public static bool IsExpired(DateTime startedUtc, DateTime nowUtc) => nowUtc >= startedUtc.AddDays(TrialDays);

    /// <summary>Whole days remaining, rounded up (so "half a day left" still reads as 1, not 0, right
    /// up until the trial actually expires) and floored at 0 (never negative - callers gate
    /// Trial-vs-TrialExpired on <see cref="IsExpired"/> separately, so this is only ever displayed
    /// for an active trial).</summary>
    public static int DaysLeft(DateTime startedUtc, DateTime nowUtc)
    {
        var remaining = startedUtc.AddDays(TrialDays) - nowUtc;
        return Math.Max(0, (int)Math.Ceiling(remaining.TotalDays));
    }

    /// <summary>Constant-time string comparison - avoids leaking HMAC-comparison timing to a local
    /// attacker probing the marker file. Ported from InfraAtlas's identical CryptographicEquals
    /// helper (net48 has no <see cref="CryptographicOperations.FixedTimeEquals"/>, added in .NET
    /// Core 3.0/.NET Standard 2.1 and never backported to Framework).</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        if (bytesA.Length != bytesB.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < bytesA.Length; i++)
        {
            diff |= bytesA[i] ^ bytesB[i];
        }

        return diff == 0;
    }
}
