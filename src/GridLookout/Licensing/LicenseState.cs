using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace GridLookout.Licensing;

/// <summary>The full, user-facing licence status vocabulary - see
/// docs/plans/2026-09-25-gridlookout-licensing.md "Product behaviour". <see cref="Licensed"/> and
/// <see cref="Trial"/> are the only "full" states (<see cref="LicenseInfo.IsFull"/>); every other
/// value gets the unlicensed banner and refuses layout changes - see <see cref="LicenseMessages"/>.</summary>
public enum LicenseStatus
{
    Licensed,
    Trial,
    TrialExpired,

    /// <summary>A licence FILE is present but fails verification (bad signature, wrong product, or
    /// bound to a different machine) - see <see cref="LicenseFileVerdict"/>. Deliberately distinct
    /// from <see cref="TrialExpired"/> even though both are "not full": an invalid file is a
    /// different, more actionable problem (fix/replace the file) than a trial simply running out
    /// with no file at all.</summary>
    Unlicensed,

    /// <summary>A validly-signed subscription licence whose <c>expiresAtUtc</c> is in the past.</summary>
    Expired,

    /// <summary>A validly-signed, unexpired licence whose <c>maintenanceUntilUtc</c> is before this
    /// build's own build date.</summary>
    MaintenanceLapsed,
}

/// <summary>Everything the UI/CLI/health.json need to describe the current licence state - see
/// <see cref="LicenseState.Current"/>.</summary>
public sealed class LicenseInfo
{
    public LicenseStatus Status { get; init; }

    public string? LicenseId { get; init; }

    public string? CustomerName { get; init; }

    public string? Edition { get; init; }

    public int? Seats { get; init; }

    public string? Term { get; init; }

    public DateTime? IssuedAtUtc { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public DateTime? MaintenanceUntilUtc { get; init; }

    /// <summary>Only meaningful for <see cref="LicenseStatus.Trial"/>/<see cref="LicenseStatus.TrialExpired"/>
    /// - when the 30-day trial window ends (or ended).</summary>
    public DateTime? TrialEndsUtc { get; init; }

    /// <summary>Whole days left in an active trial - only set for <see cref="LicenseStatus.Trial"/>.</summary>
    public int? TrialDaysLeft { get; init; }

    /// <summary>Human-readable explanation of <see cref="Status"/> - what <c>--license</c>/health.json
    /// show verbatim.</summary>
    public string Message { get; init; } = string.Empty;

    public string LicenseFilePath { get; init; } = string.Empty;

    public string MachineFingerprint { get; init; } = string.Empty;

    /// <summary>Licensed and Trial are the only "full" states - see
    /// docs/plans/2026-09-25-gridlookout-licensing.md "Product behaviour".</summary>
    public bool IsFull => Status is LicenseStatus.Licensed or LicenseStatus.Trial;
}

/// <summary>Reads the AssemblyMetadata("BuildDateUtc", ...) attribute GridLookout.csproj generates
/// (see that file's BuildDateUtc property) through ONE static accessor - see
/// docs/plans/2026-09-25-gridlookout-licensing.md "Risks": "Build date metadata must not break
/// deterministic tests; read through one static accessor with a test override." Tests never call
/// <see cref="ReadBuildDateUtc"/> at all - they pass an explicit <c>buildDateUtc</c> straight into
/// <see cref="LicenseState"/>'s constructor instead, which is the "test override" referred to
/// above.</summary>
public static class BuildMetadata
{
    public static DateTime ReadBuildDateUtc()
    {
        try
        {
            var value = typeof(BuildMetadata).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildDateUtc")
                ?.Value;

            if (value is not null
                && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // Reflection over this process's own assembly should never fail, but this value feeds a
            // date comparison at startup - degrade rather than risk bricking boot over it.
        }

        // No metadata present (e.g. a dev build whose csproj item somehow didn't apply) - fall back
        // to "today", the conservative direction: a licence's maintenance window then reads as
        // covering this run rather than lapsed, so a missing build-date tag can never manufacture a
        // false MaintenanceLapsed banner on a dev box.
        return DateTime.UtcNow.Date;
    }
}

/// <summary>Formats the fixed strings docs/plans/2026-09-25-gridlookout-licensing.md specifies
/// verbatim for the banner, the refused-layout-change log line, and the licensed/trial startup log
/// lines - centralised here so WallForm/Program.cs never duplicate the wording.</summary>
public static class LicenseMessages
{
    private const string BuyLink = "Buy a licence at gridlookout.it42.hr";

    /// <summary>The fixed red banner strip text for every "not full" status.</summary>
    public static string BannerText(LicenseInfo license)
    {
        var detail = license.Status switch
        {
            LicenseStatus.TrialExpired when license.TrialEndsUtc is DateTime endedUtc =>
                $"trial ended on {endedUtc:yyyy-MM-dd}",
            LicenseStatus.TrialExpired => "trial ended",
            LicenseStatus.Expired when license.ExpiresAtUtc is DateTime expiredUtc =>
                $"licence expired on {expiredUtc:yyyy-MM-dd}",
            LicenseStatus.Expired => "licence expired",
            LicenseStatus.MaintenanceLapsed when license.MaintenanceUntilUtc is DateTime maintUtc =>
                $"this build is newer than the licence's maintenance coverage (ended {maintUtc:yyyy-MM-dd})",
            LicenseStatus.MaintenanceLapsed => "this build is newer than the licence's maintenance coverage",
            _ => "no valid licence is installed",
        };

        return $"UNLICENSED - GridLookout {detail}. {BuyLink}";
    }

    /// <summary>The Warning log line for a layout change refused by <see cref="LicenseState"/> - see
    /// Program.cs's refresh-tick handling. Fixed format regardless of status: "GridLookout is
    /// unlicensed (status X)".</summary>
    public static string RefusalWarning(LicenseInfo license) =>
        $"Layout change refused: GridLookout is unlicensed (status {license.Status}). {BuyLink}";

    /// <summary>Startup Info log line for <see cref="LicenseStatus.Licensed"/>.</summary>
    public static string LicensedStartupLine(LicenseInfo license)
    {
        var seatsText = license.Seats is int seats ? $"{seats} seat{(seats == 1 ? string.Empty : "s")}" : "seats unknown";
        var maintenanceText = license.MaintenanceUntilUtc is DateTime maintUtc
            ? $"maintenance until {maintUtc:yyyy-MM-dd}"
            : "no maintenance-end date recorded";
        return $"Licence {license.LicenseId} for {license.CustomerName}, {license.Edition} ({seatsText}), {license.Term}, {maintenanceText}.";
    }

    /// <summary>Startup Info log line for <see cref="LicenseStatus.Trial"/> - includes the days-left
    /// count that also drives the header's "Trial - N days left" line and the escalation to Warning
    /// when 7 or fewer days remain (see Program.cs).</summary>
    public static string TrialStartupLine(LicenseInfo license) =>
        $"GridLookout is running on a trial licence - {TrialDaysLeftText(license.TrialDaysLeft)} (ends {license.TrialEndsUtc:yyyy-MM-dd}). {BuyLink}";

    /// <summary>The small header-strip suffix for an active trial: "Trial - 23 days left".</summary>
    public static string TrialHeaderSuffix(LicenseInfo license) => $"Trial - {TrialDaysLeftText(license.TrialDaysLeft)}";

    private static string TrialDaysLeftText(int? daysLeft)
    {
        var days = daysLeft ?? 0;
        return $"{days} day{(days == 1 ? string.Empty : "s")} left";
    }
}

/// <summary>
/// Resolves gridlookout.lic (lookup order: configured LicensePath -&gt; &lt;stateDir&gt;\gridlookout.lic
/// -&gt; &lt;exeDir&gt;\gridlookout.lic), verifies it via <see cref="LicenseVerifier"/>, falls back to
/// trial-marker evaluation (<see cref="TrialMarker"/>) when no valid file is present, and caches the
/// resulting <see cref="LicenseInfo"/> - see docs/plans/2026-09-25-gridlookout-licensing.md "Product
/// behaviour" for the full status vocabulary and lookup/trial rules this implements.
///
/// One instance lives for the process lifetime (constructed once in Program.cs's Main, after config
/// load); <see cref="RefreshIfFileChanged"/> is called from the periodic refresh tick so an operator
/// installing a licence via <c>--license-install</c> is picked up within ConfigRefreshSeconds with no
/// restart, and <see cref="PulseTrialMarker"/> advances the trial marker's anti-clock-rollback
/// high-water mark at most once an hour while a trial is active.
/// </summary>
public sealed class LicenseState
{
    private readonly string _configuredLicensePath;
    private readonly string _stateDir;
    private readonly string _exeDir;
    private readonly RSAParameters _publicKey;
    private readonly Func<DateTime> _now;
    private readonly DateTime _buildDateUtc;
    private readonly string _machineFingerprint;
    private readonly Action<string>? _logInfo;
    private readonly Action<string>? _logWarning;

    private static readonly TimeSpan MarkerPulseInterval = TimeSpan.FromHours(1);

    private DateTime? _lastLicenseFileWriteUtc;
    private string? _lastResolvedLicensePath;

    /// <param name="configuredLicensePath">WallConfig.LicensePath - empty/whitespace means "not
    /// configured", falling through to the state-dir/exe-dir lookup.</param>
    /// <param name="stateDir">The writable state directory (see Config.StateDirectory) - the second
    /// lookup location and where the trial marker always lives.</param>
    /// <param name="exeDir">The exe directory - the third/last lookup location.</param>
    /// <param name="publicKey">Normally <see cref="LicensePublicKey.GetParameters"/>; tests inject a
    /// throwaway keypair's public half.</param>
    /// <param name="nowProvider">Defaults to real <see cref="DateTime.UtcNow"/>; tests inject a fixed
    /// clock.</param>
    /// <param name="buildDateUtc">Defaults to <see cref="BuildMetadata.ReadBuildDateUtc"/>; tests
    /// inject an explicit date - see that type's own doc comment.</param>
    /// <param name="machineFingerprintOverride">Defaults to <see cref="Licensing.MachineFingerprint.Compute"/>;
    /// tests inject a fixed value so trial-marker HMAC tests are deterministic and machine-independent.</param>
    public LicenseState(
        string? configuredLicensePath,
        string stateDir,
        string exeDir,
        RSAParameters publicKey,
        Func<DateTime>? nowProvider = null,
        DateTime? buildDateUtc = null,
        string? machineFingerprintOverride = null,
        Action<string>? logInfo = null,
        Action<string>? logWarning = null)
    {
        _configuredLicensePath = configuredLicensePath ?? string.Empty;
        _stateDir = stateDir;
        _exeDir = exeDir;
        _publicKey = publicKey;
        _now = nowProvider ?? (() => DateTime.UtcNow);
        _buildDateUtc = buildDateUtc ?? BuildMetadata.ReadBuildDateUtc();
        _machineFingerprint = machineFingerprintOverride ?? Licensing.MachineFingerprint.Compute();
        _logInfo = logInfo;
        _logWarning = logWarning;

        Current = Refresh();
    }

    public LicenseInfo Current { get; private set; }

    /// <summary>Lookup order: configured path (absolute, from camerawall.json) -&gt;
    /// &lt;stateDir&gt;\gridlookout.lic -&gt; &lt;exeDir&gt;\gridlookout.lic. The state-dir candidate
    /// wins over the exe-dir one whenever IT exists, regardless of the exe-dir file's existence -
    /// matching every other piece of GridLookout's mutable state (see Config.StateDirectory), the
    /// state dir is where <c>--license-install</c> writes, so a wall running from a read-only exe dir
    /// still picks up an installed licence.</summary>
    public string ResolveLicensePath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredLicensePath))
        {
            return _configuredLicensePath;
        }

        var stateCandidate = Path.Combine(_stateDir, "gridlookout.lic");
        if (File.Exists(stateCandidate))
        {
            return stateCandidate;
        }

        return Path.Combine(_exeDir, "gridlookout.lic");
    }

    /// <summary>Re-evaluates licence/trial status from scratch and updates <see cref="Current"/>.
    /// Always safe to call - every failure mode (unreadable file, corrupt trial marker) degrades to a
    /// well-defined status rather than throwing.</summary>
    public LicenseInfo Refresh()
    {
        var licensePath = ResolveLicensePath();
        _lastResolvedLicensePath = licensePath;
        var nowUtc = _now();

        if (File.Exists(licensePath))
        {
            try
            {
                var content = File.ReadAllText(licensePath);
                var result = LicenseVerifier.Verify(content, _publicKey, nowUtc, _buildDateUtc, _machineFingerprint);
                _lastLicenseFileWriteUtc = SafeGetLastWriteTimeUtc(licensePath);
                Current = BuildFromVerification(result, licensePath);
                return Current;
            }
            catch (IOException ex)
            {
                _logWarning?.Invoke($"Licence file '{licensePath}' could not be read ({ex.GetType().Name}: {ex.Message}) - evaluating trial status instead.");
            }
        }
        else
        {
            _lastLicenseFileWriteUtc = null;
        }

        Current = EvaluateTrial(nowUtc, licensePath);
        return Current;
    }

    /// <summary>Cheap per-tick check: re-runs <see cref="Refresh"/> only when the resolved licence
    /// path or its mtime changed since the last check - what lets <c>--license-install</c> take
    /// effect on a running wall within ConfigRefreshSeconds with no restart (Program.cs's refresh
    /// timer calls this every tick). Returns true when a refresh actually ran.</summary>
    public bool RefreshIfFileChanged()
    {
        var path = ResolveLicensePath();
        var mtime = SafeGetLastWriteTimeUtc(path);

        if (path == _lastResolvedLicensePath && mtime == _lastLicenseFileWriteUtc)
        {
            return false;
        }

        Refresh();
        return true;
    }

    /// <summary>Advances the trial marker's LastSeenUtc high-water mark, at most once per hour - see
    /// docs/plans/2026-09-25-gridlookout-licensing.md: "lastSeenUtc is refreshed at most once per
    /// hour by the UI pulse." The gate is read from the marker FILE's own LastSeenUtc (not an
    /// in-memory timestamp) - stateless and self-correcting across a restart, and means the very
    /// first pulse after a trial has just started still waits out the interval rather than writing
    /// again moments after <see cref="TrialMarker.CreateFresh"/> already did. A no-op outside an
    /// active trial (Licensed/Expired/etc never touch the marker) and when the marker file is
    /// missing/tampered (a fresh <see cref="Refresh"/> will re-evaluate and either recreate it or
    /// flip to TrialExpired, whichever is correct - this method never creates or repairs a marker
    /// itself).</summary>
    public void PulseTrialMarker()
    {
        if (Current.Status != LicenseStatus.Trial)
        {
            return;
        }

        var markerPath = Path.Combine(_stateDir, TrialMarker.FileName);
        if (!File.Exists(markerPath))
        {
            return;
        }

        try
        {
            var raw = File.ReadAllText(markerPath);
            var marker = TrialMarker.TryRead(raw, _machineFingerprint);
            if (marker is null)
            {
                return;
            }

            var nowUtc = _now();
            if (nowUtc - marker.LastSeenUtc < MarkerPulseInterval)
            {
                return;
            }

            var updated = TrialMarker.WithLastSeen(marker, _machineFingerprint, nowUtc);
            TryWriteMarker(markerPath, updated);
        }
        catch (IOException)
        {
            // Best-effort - the next pulse tries again.
        }
    }

    private LicenseInfo BuildFromVerification(LicenseFileResult result, string licensePath)
    {
        var payload = result.Payload;

        LicenseStatus status = result.Verdict switch
        {
            LicenseFileVerdict.Valid => LicenseStatus.Licensed,
            LicenseFileVerdict.Expired => LicenseStatus.Expired,
            LicenseFileVerdict.MaintenanceLapsed => LicenseStatus.MaintenanceLapsed,
            _ => LicenseStatus.Unlicensed,
        };

        if (status is LicenseStatus.Unlicensed && result.Verdict != LicenseFileVerdict.InvalidFormat)
        {
            _logWarning?.Invoke($"Licence file '{licensePath}' is invalid: {result.Message}");
        }

        return new LicenseInfo
        {
            Status = status,
            LicenseId = payload?.LicenseId,
            CustomerName = payload?.CustomerName,
            Edition = payload?.Edition,
            Seats = payload?.Seats,
            Term = payload?.Term,
            IssuedAtUtc = payload?.IssuedAtUtc,
            ExpiresAtUtc = payload?.ExpiresAtUtc,
            MaintenanceUntilUtc = payload?.MaintenanceUntilUtc,
            Message = result.Message,
            LicenseFilePath = licensePath,
            MachineFingerprint = _machineFingerprint,
        };
    }

    private LicenseInfo EvaluateTrial(DateTime nowUtc, string licensePath)
    {
        var markerPath = Path.Combine(_stateDir, TrialMarker.FileName);

        TrialMarkerFile? marker = null;
        bool markerPresentButUnusable = false;

        if (File.Exists(markerPath))
        {
            string raw;
            try
            {
                raw = File.ReadAllText(markerPath);
            }
            catch (IOException)
            {
                raw = string.Empty;
            }

            if (!string.IsNullOrEmpty(raw))
            {
                marker = TrialMarker.TryRead(raw, _machineFingerprint);
                markerPresentButUnusable = marker is null;
            }
        }

        if (marker is null)
        {
            if (markerPresentButUnusable)
            {
                _logWarning?.Invoke($"Trial marker '{markerPath}' is unreadable, tampered, or was copied from a different machine - treating the trial as expired.");
                return BuildTrialExpiredInfo(nowUtc, licensePath, trialEndsUtc: null);
            }

            var fresh = TrialMarker.CreateFresh(_machineFingerprint, nowUtc);
            TryWriteMarker(markerPath, fresh);
            _logInfo?.Invoke($"GridLookout trial started ({TrialMarker.TrialDays} days) - marker written to '{markerPath}'.");
            return BuildTrialInfo(fresh.StartedUtc, nowUtc, licensePath);
        }

        if (TrialMarker.IsClockRollback(nowUtc, marker.LastSeenUtc))
        {
            _logWarning?.Invoke($"System clock rollback detected (now {nowUtc:O} is more than 24h behind the trial marker's last-seen {marker.LastSeenUtc:O}, started {marker.StartedUtc:O}) - treating the trial as expired.");
            return BuildTrialExpiredInfo(nowUtc, licensePath, marker.StartedUtc.AddDays(TrialMarker.TrialDays));
        }

        if (TrialMarker.IsExpired(marker.StartedUtc, nowUtc))
        {
            return BuildTrialExpiredInfo(nowUtc, licensePath, marker.StartedUtc.AddDays(TrialMarker.TrialDays));
        }

        return BuildTrialInfo(marker.StartedUtc, nowUtc, licensePath);
    }

    private LicenseInfo BuildTrialInfo(DateTime startedUtc, DateTime nowUtc, string licensePath)
    {
        var trialEndsUtc = startedUtc.AddDays(TrialMarker.TrialDays);
        var daysLeft = TrialMarker.DaysLeft(startedUtc, nowUtc);
        return new LicenseInfo
        {
            Status = LicenseStatus.Trial,
            TrialEndsUtc = trialEndsUtc,
            TrialDaysLeft = daysLeft,
            Message = $"Trial active - {daysLeft} day(s) left, ends {trialEndsUtc:yyyy-MM-dd}.",
            LicenseFilePath = licensePath,
            MachineFingerprint = _machineFingerprint,
        };
    }

    private LicenseInfo BuildTrialExpiredInfo(DateTime nowUtc, string licensePath, DateTime? trialEndsUtc)
    {
        return new LicenseInfo
        {
            Status = LicenseStatus.TrialExpired,
            TrialEndsUtc = trialEndsUtc,
            Message = trialEndsUtc is DateTime ended
                ? $"Trial ended on {ended:yyyy-MM-dd}."
                : "Trial has ended.",
            LicenseFilePath = licensePath,
            MachineFingerprint = _machineFingerprint,
        };
    }

    private void TryWriteMarker(string markerPath, TrialMarkerFile marker)
    {
        try
        {
            Directory.CreateDirectory(_stateDir);
            File.WriteAllText(markerPath, TrialMarker.Serialize(marker));
        }
        catch (IOException ex)
        {
            _logWarning?.Invoke($"Could not write trial marker '{markerPath}' ({ex.GetType().Name}: {ex.Message}) - trial state for this run is in-memory only and will be re-evaluated fresh on the next start.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logWarning?.Invoke($"Could not write trial marker '{markerPath}' ({ex.GetType().Name}: {ex.Message}) - trial state for this run is in-memory only and will be re-evaluated fresh on the next start.");
        }
    }

    private static DateTime? SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
