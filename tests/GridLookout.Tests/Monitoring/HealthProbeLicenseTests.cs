using System;
using System.IO;
using System.Text.Json;
using GridLookout.Config;
using GridLookout.Licensing;
using GridLookout.Monitoring;
using GridLookout.Tests.Config;
using GridLookout.Tests.Licensing;
using Xunit;

namespace GridLookout.Tests.Monitoring;

/// <summary>Licensing (1.1.0): --health-probe must print the licence status AFTER the health
/// verdict line, never fold it into the verdict/exit code - see HealthProbe.Run's own doc comment.</summary>
public sealed class HealthProbeLicenseTests : IDisposable
{
    private readonly string _root;
    private readonly LicenseTestSigner _signer = new();

    public HealthProbeLicenseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "gridlookout-healthprobe-license-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _signer.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private (WallConfigLoader Loader, FakeStateDirectory StateDirectory) BuildLoader() =>
        (new WallConfigLoader(new FakeSecretProtector(), new FakeStateDirectory(writable: true, _root)), new FakeStateDirectory(writable: true, _root));

    [Fact]
    public void Run_WithNoLicenseState_PrintsOnlyTheVerdictLine()
    {
        var (loader, stateDirectory) = BuildLoader();
        var writer = new StringWriter();

        HealthProbe.Run(stateDirectory, _root, new WallConfig(), loader, writer, licenseState: null);

        var lines = writer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void Run_WithLicenseState_PrintsALicenseLineAfterTheVerdict()
    {
        var (loader, stateDirectory) = BuildLoader();
        var writer = new StringWriter();
        var licenseState = new LicenseState(
            configuredLicensePath: null, _root, _root, _signer.PublicKey,
            nowProvider: () => new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            buildDateUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            machineFingerprintOverride: "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD");

        HealthProbe.Run(stateDirectory, _root, new WallConfig(), loader, writer, licenseState: licenseState);

        var lines = writer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var verdict = JsonDocument.Parse(lines[0]);
        Assert.True(verdict.RootElement.TryGetProperty("exitCode", out _));

        using var licenseLine = JsonDocument.Parse(lines[1]);
        Assert.Equal("Trial", licenseLine.RootElement.GetProperty("licenseStatus").GetString());
        Assert.True(licenseLine.RootElement.GetProperty("licenseIsFull").GetBoolean());
    }

    [Fact]
    public void Run_LicenseLine_NeverAffectsTheExitCode_EvenWhenUnlicensed()
    {
        var (loader, stateDirectory) = BuildLoader();
        var writer = new StringWriter();

        // Pre-age the trial marker by 40 days (the Proof section's own scenario) so LicenseState
        // actually lands in TrialExpired rather than starting a brand-new trial "now" - no
        // health.json at all means the verdict is "absent" (exit 3) regardless; the point of this
        // test is that adding an UNLICENSED licenseState changes nothing about that exit code.
        const string fingerprint = "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD";
        var baseline = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var agedMarker = TrialMarker.CreateFresh(fingerprint, baseline);
        File.WriteAllText(Path.Combine(_root, TrialMarker.FileName), TrialMarker.Serialize(agedMarker));

        var licenseState = new LicenseState(
            configuredLicensePath: null, _root, _root, _signer.PublicKey,
            nowProvider: () => baseline.AddDays(40),
            buildDateUtc: baseline,
            machineFingerprintOverride: fingerprint);

        var exitCodeWithoutLicense = HealthProbe.Run(stateDirectory, _root, new WallConfig(), loader, new StringWriter(), licenseState: null);
        var exitCodeWithLicense = HealthProbe.Run(stateDirectory, _root, new WallConfig(), loader, writer, licenseState: licenseState);

        Assert.Equal(exitCodeWithoutLicense, exitCodeWithLicense);
        Assert.Contains("\"licenseStatus\":\"TrialExpired\"", writer.ToString());
    }
}
