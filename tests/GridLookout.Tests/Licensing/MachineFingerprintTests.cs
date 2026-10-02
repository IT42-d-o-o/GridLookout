using System.Text.RegularExpressions;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public class MachineFingerprintTests
{
    private static readonly Regex FingerprintFormat = new(
        @"^[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}-[0-9A-F]{8}$|^FALLBACK-[0-9A-F]{28}$",
        RegexOptions.Compiled);

    [Fact]
    public void Compute_NeverThrows_AndIsNeverEmpty()
    {
        var fingerprint = MachineFingerprint.Compute();

        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
    }

    [Fact]
    public void Compute_MatchesTheDocumentedFormat()
    {
        var fingerprint = MachineFingerprint.Compute();

        Assert.Matches(FingerprintFormat, fingerprint);
    }

    [Fact]
    public void Compute_IsStableAcrossCallsOnTheSameMachine()
    {
        var first = MachineFingerprint.Compute();
        var second = MachineFingerprint.Compute();

        Assert.Equal(first, second);
    }
}
