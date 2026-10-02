using System;
using System.IO;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public sealed class LicenseStateTests : IDisposable
{
    private readonly string _root;
    private readonly string _stateDir;
    private readonly string _exeDir;
    private readonly LicenseTestSigner _signer = new();
    private DateTime _now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    public LicenseStateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gridlookout-license-tests-" + Guid.NewGuid().ToString("N"));
        _stateDir = Path.Combine(_root, "state");
        _exeDir = Path.Combine(_root, "exe");
        Directory.CreateDirectory(_stateDir);
        Directory.CreateDirectory(_exeDir);
    }

    public void Dispose()
    {
        _signer.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private LicenseState BuildState(string? configuredPath = null, DateTime? buildDateUtc = null, string machineFingerprint = "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD") =>
        new(configuredPath, _stateDir, _exeDir, _signer.PublicKey,
            nowProvider: () => _now,
            buildDateUtc: buildDateUtc ?? _now,
            machineFingerprintOverride: machineFingerprint);

    // -----------------------------------------------------------------------
    // Lookup order
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveLicensePath_ConfiguredPathWinsOutright()
    {
        var configuredPath = Path.Combine(_root, "elsewhere.lic");
        var state = BuildState(configuredPath: configuredPath);

        Assert.Equal(configuredPath, state.ResolveLicensePath());
    }

    [Fact]
    public void ResolveLicensePath_StateDirWinsOverExeDir_WhenStateDirFileExists()
    {
        File.WriteAllText(Path.Combine(_stateDir, "gridlookout.lic"), "state-dir-copy");
        File.WriteAllText(Path.Combine(_exeDir, "gridlookout.lic"), "exe-dir-copy");

        var state = BuildState();

        Assert.Equal(Path.Combine(_stateDir, "gridlookout.lic"), state.ResolveLicensePath());
    }

    [Fact]
    public void ResolveLicensePath_FallsBackToExeDir_WhenStateDirFileAbsent()
    {
        var state = BuildState();

        Assert.Equal(Path.Combine(_exeDir, "gridlookout.lic"), state.ResolveLicensePath());
    }

    // -----------------------------------------------------------------------
    // No licence file - trial lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void Construct_NoLicenceNoMarker_StartsAFreshTrial_AndWritesMarker()
    {
        var state = BuildState();

        Assert.Equal(LicenseStatus.Trial, state.Current.Status);
        Assert.Equal(30, state.Current.TrialDaysLeft);
        Assert.True(File.Exists(Path.Combine(_stateDir, TrialMarker.FileName)));
    }

    [Fact]
    public void Construct_NoLicenceNoMarker_IsFull()
    {
        var state = BuildState();

        Assert.True(state.Current.IsFull);
    }

    [Fact]
    public void Refresh_ExistingFreshMarker_StaysTrial_WithDecreasingDaysLeft()
    {
        var state = BuildState();
        Assert.Equal(30, state.Current.TrialDaysLeft);

        _now = _now.AddDays(5);
        state.Refresh();

        Assert.Equal(LicenseStatus.Trial, state.Current.Status);
        Assert.Equal(25, state.Current.TrialDaysLeft);
    }

    [Fact]
    public void Refresh_MarkerAgedPastTrialWindow_IsTrialExpired()
    {
        var state = BuildState();
        _now = _now.AddDays(40);

        state.Refresh();

        Assert.Equal(LicenseStatus.TrialExpired, state.Current.Status);
        Assert.False(state.Current.IsFull);
    }

    [Fact]
    public void Refresh_TamperedMarker_IsTrialExpired()
    {
        var markerPath = Path.Combine(_stateDir, TrialMarker.FileName);
        File.WriteAllText(markerPath, "{ \"startedUtc\": \"2020-01-01T00:00:00Z\", \"lastSeenUtc\": \"2020-01-01T00:00:00Z\", \"hmac\": \"bogus\" }");

        var state = BuildState();

        Assert.Equal(LicenseStatus.TrialExpired, state.Current.Status);
    }

    [Fact]
    public void Refresh_ClockRolledBack_IsTrialExpired()
    {
        var state = BuildState();
        Assert.Equal(LicenseStatus.Trial, state.Current.Status);

        // Pulse lastSeenUtc forward first so there is a high-water mark to roll back behind.
        _now = _now.AddDays(2);
        state.PulseTrialMarker();

        // Now roll the clock back more than the 24h tolerance.
        _now = _now.AddDays(-2).AddHours(-1);
        state.Refresh();

        Assert.Equal(LicenseStatus.TrialExpired, state.Current.Status);
    }

    // -----------------------------------------------------------------------
    // Licence file present
    // -----------------------------------------------------------------------

    [Fact]
    public void Refresh_ValidLicenceFile_IsLicensed_WithPayloadFields()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense(licenseId: "GL-2026-000456", customerName: "Contoso"));

        var state = BuildState();

        Assert.Equal(LicenseStatus.Licensed, state.Current.Status);
        Assert.True(state.Current.IsFull);
        Assert.Equal("GL-2026-000456", state.Current.LicenseId);
        Assert.Equal("Contoso", state.Current.CustomerName);
    }

    [Fact]
    public void Refresh_InvalidLicenceFile_IsUnlicensed_NotTrial()
    {
        WriteLicenseFile("{ \"payload\": \"abc\", \"signature\": \"not-a-real-signature\" }");

        var state = BuildState();

        Assert.Equal(LicenseStatus.Unlicensed, state.Current.Status);
        Assert.False(state.Current.IsFull);
    }

    [Fact]
    public void Refresh_ExpiredSubscriptionLicenceFile_IsExpired()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense(term: "subscription", expiresAtUtc: _now.AddDays(-1)));

        var state = BuildState();

        Assert.Equal(LicenseStatus.Expired, state.Current.Status);
        Assert.False(state.Current.IsFull);
    }

    [Fact]
    public void Refresh_MaintenanceLapsedLicenceFile_IsMaintenanceLapsed()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense(maintenanceUntilUtc: _now.AddDays(-1)));

        var state = BuildState();

        Assert.Equal(LicenseStatus.MaintenanceLapsed, state.Current.Status);
        Assert.False(state.Current.IsFull);
    }

    [Fact]
    public void Refresh_LicenceFileTakesPriorityOverTrial_EvenWhenTrialWouldStillBeActive()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense());

        var state = BuildState();

        Assert.Equal(LicenseStatus.Licensed, state.Current.Status);
        // No trial marker should have been created - a valid licence short-circuits trial evaluation.
        Assert.False(File.Exists(Path.Combine(_stateDir, TrialMarker.FileName)));
    }

    // -----------------------------------------------------------------------
    // RefreshIfFileChanged / --license-install "no restart" contract
    // -----------------------------------------------------------------------

    [Fact]
    public void RefreshIfFileChanged_ReturnsFalse_WhenNothingChanged()
    {
        var state = BuildState();

        Assert.False(state.RefreshIfFileChanged());
    }

    [Fact]
    public void RefreshIfFileChanged_DetectsANewlyInstalledLicenceFile_AndFlipsStatus()
    {
        var state = BuildState();
        Assert.Equal(LicenseStatus.Trial, state.Current.Status);

        WriteLicenseFile(_signer.SignGridLookoutLicense());

        var changed = state.RefreshIfFileChanged();

        Assert.True(changed);
        Assert.Equal(LicenseStatus.Licensed, state.Current.Status);
    }

    [Fact]
    public void RefreshIfFileChanged_DetectsAnEditedLicenceFile_ByMtime()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense(licenseId: "GL-original"));
        var state = BuildState();
        Assert.Equal("GL-original", state.Current.LicenseId);

        // Force a distinct, later mtime - some filesystems have coarse (~2s) mtime resolution.
        System.Threading.Thread.Sleep(50);
        WriteLicenseFile(_signer.SignGridLookoutLicense(licenseId: "GL-updated"));
        File.SetLastWriteTimeUtc(state.ResolveLicensePath(), DateTime.UtcNow.AddSeconds(5));

        var changed = state.RefreshIfFileChanged();

        Assert.True(changed);
        Assert.Equal("GL-updated", state.Current.LicenseId);
    }

    // -----------------------------------------------------------------------
    // PulseTrialMarker
    // -----------------------------------------------------------------------

    [Fact]
    public void PulseTrialMarker_NoOp_WhenNotOnTrial()
    {
        WriteLicenseFile(_signer.SignGridLookoutLicense());
        var state = BuildState();

        // Must not throw and must not create a trial marker for a fully-licensed run.
        state.PulseTrialMarker();

        Assert.False(File.Exists(Path.Combine(_stateDir, TrialMarker.FileName)));
    }

    [Fact]
    public void PulseTrialMarker_AdvancesLastSeen_PreventingAFalseClockRollbackLater()
    {
        var state = BuildState();

        _now = _now.AddMinutes(30);
        state.PulseTrialMarker(); // within the first hour - no-op per the once-per-hour gate

        _now = _now.AddHours(2);
        state.PulseTrialMarker(); // past the gate - should advance LastSeenUtc

        var markerJson = File.ReadAllText(Path.Combine(_stateDir, TrialMarker.FileName));
        var marker = TrialMarker.TryRead(markerJson, "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD");
        Assert.NotNull(marker);
        Assert.Equal(_now, marker!.LastSeenUtc);
    }

    private void WriteLicenseFile(string content) => File.WriteAllText(Path.Combine(_stateDir, "gridlookout.lic"), content);
}
