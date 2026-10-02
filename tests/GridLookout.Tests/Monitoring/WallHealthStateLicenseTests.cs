using System;
using System.Text.Json;
using GridLookout.Licensing;
using GridLookout.Monitoring;
using Xunit;

namespace GridLookout.Tests.Monitoring;

/// <summary>Licensing (1.1.0): WallHealthState.License round-trips through the SAME
/// HealthJsonOptions both the controller's writer and the probe's reader use - see health.json
/// serialisation's own doc comment on HealthJsonOptions.</summary>
public class WallHealthStateLicenseTests
{
    [Fact]
    public void License_RoundTrips_WithEnumAsString()
    {
        var state = new WallHealthState
        {
            License = new LicenseHealth
            {
                Status = LicenseStatus.Trial,
                TrialEndsUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc),
                Message = "Trial active - 23 day(s) left, ends 2026-10-25.",
            },
        };

        var json = JsonSerializer.Serialize(state, HealthJsonOptions.Default);

        Assert.Contains("\"License\"", json);
        Assert.Contains("\"Status\": \"Trial\"", json);

        var roundTripped = JsonSerializer.Deserialize<WallHealthState>(json, HealthJsonOptions.Default);
        Assert.NotNull(roundTripped!.License);
        Assert.Equal(LicenseStatus.Trial, roundTripped.License!.Status);
        Assert.Equal(state.License.TrialEndsUtc, roundTripped.License.TrialEndsUtc);
    }

    [Fact]
    public void License_IsNull_WhenNeverSet_AndSerializesWithoutThrowing()
    {
        var state = new WallHealthState();

        var json = JsonSerializer.Serialize(state, HealthJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<WallHealthState>(json, HealthJsonOptions.Default);

        Assert.Null(roundTripped!.License);
    }

    [Fact]
    public void SchemaVersion_StaysOne_LicenseIsAdditive()
    {
        var state = new WallHealthState { License = new LicenseHealth { Status = LicenseStatus.Licensed } };

        Assert.Equal(1, state.SchemaVersion);
    }
}
