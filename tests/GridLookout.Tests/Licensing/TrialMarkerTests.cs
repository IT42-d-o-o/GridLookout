using System;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public class TrialMarkerTests
{
    private const string Fingerprint = "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD";
    private const string OtherFingerprint = "11111111-22222222-33333333-44444444";
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateFresh_RoundTripsThroughSerializeAndTryRead()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        var json = TrialMarker.Serialize(fresh);

        var read = TrialMarker.TryRead(json, Fingerprint);

        Assert.NotNull(read);
        Assert.Equal(Now, read!.StartedUtc);
        Assert.Equal(Now, read.LastSeenUtc);
    }

    [Fact]
    public void TryRead_TamperedHmac_ReturnsNull()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        var tampered = new TrialMarkerFile { StartedUtc = fresh.StartedUtc, LastSeenUtc = fresh.LastSeenUtc, Hmac = "not-the-real-hmac" };
        var json = TrialMarker.Serialize(tampered);

        var read = TrialMarker.TryRead(json, Fingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void TryRead_StartedUtcEditedWithoutRecomputingHmac_ReturnsNull()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        // Attacker rolls the recorded start date back to "extend" the trial, but keeps the OLD HMAC
        // (computed over the original StartedUtc) - the MAC must catch this.
        var tampered = new TrialMarkerFile { StartedUtc = Now.AddDays(-100), LastSeenUtc = fresh.LastSeenUtc, Hmac = fresh.Hmac };
        var json = TrialMarker.Serialize(tampered);

        var read = TrialMarker.TryRead(json, Fingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void TryRead_LastSeenUtcEditedWithoutRecomputingHmac_ReturnsNull()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        // Attacker edits LastSeenUtc alone (e.g. to defeat clock-rollback detection) without
        // recomputing the HMAC - must also be caught, since the MAC covers both fields.
        var tampered = new TrialMarkerFile { StartedUtc = fresh.StartedUtc, LastSeenUtc = Now.AddDays(50), Hmac = fresh.Hmac };
        var json = TrialMarker.Serialize(tampered);

        var read = TrialMarker.TryRead(json, Fingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void TryRead_CopiedToAnotherMachine_ReturnsNull()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        var json = TrialMarker.Serialize(fresh);

        // Same bytes, different machine fingerprint at read time - simulates copying the marker file
        // to a different machine to "reset" a trial there.
        var read = TrialMarker.TryRead(json, OtherFingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void TryRead_MalformedJson_ReturnsNull()
    {
        var read = TrialMarker.TryRead("{ this is not valid json", Fingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void TryRead_EmptyHmac_ReturnsNull()
    {
        var noHmac = new TrialMarkerFile { StartedUtc = Now, LastSeenUtc = Now, Hmac = string.Empty };
        var json = TrialMarker.Serialize(noHmac);

        var read = TrialMarker.TryRead(json, Fingerprint);

        Assert.Null(read);
    }

    [Fact]
    public void WithLastSeen_AdvancesLastSeenAndKeepsStartedUtc_AndStaysVerifiable()
    {
        var fresh = TrialMarker.CreateFresh(Fingerprint, Now);
        var advanced = TrialMarker.WithLastSeen(fresh, Fingerprint, Now.AddHours(1));

        Assert.Equal(fresh.StartedUtc, advanced.StartedUtc);
        Assert.Equal(Now.AddHours(1), advanced.LastSeenUtc);
        Assert.True(TrialMarker.VerifyHmac(advanced, Fingerprint));
    }

    [Fact]
    public void IsExpired_FalseBeforeTrialDaysElapse()
    {
        Assert.False(TrialMarker.IsExpired(Now, Now.AddDays(TrialMarker.TrialDays - 1)));
    }

    [Fact]
    public void IsExpired_TrueOnceTrialDaysElapse()
    {
        Assert.True(TrialMarker.IsExpired(Now, Now.AddDays(TrialMarker.TrialDays)));
    }

    [Fact]
    public void IsExpired_TrueLongAfterTrialDaysElapse()
    {
        // Proof section's own scenario: a trial marker aged 40 days.
        Assert.True(TrialMarker.IsExpired(Now, Now.AddDays(40)));
    }

    [Fact]
    public void IsClockRollback_FalseForOrdinaryForwardTimeMovement()
    {
        Assert.False(TrialMarker.IsClockRollback(Now.AddDays(1), Now));
    }

    [Fact]
    public void IsClockRollback_FalseForSmallBackwardJitterWithinTolerance()
    {
        Assert.False(TrialMarker.IsClockRollback(Now.AddHours(-1), Now));
    }

    [Fact]
    public void IsClockRollback_TrueForALargeBackwardJump()
    {
        Assert.True(TrialMarker.IsClockRollback(Now.AddDays(-2), Now));
    }

    [Fact]
    public void DaysLeft_RoundsUpAndNeverGoesNegative()
    {
        Assert.Equal(30, TrialMarker.DaysLeft(Now, Now));
        Assert.Equal(1, TrialMarker.DaysLeft(Now, Now.AddDays(30).AddHours(-1)));
        Assert.Equal(0, TrialMarker.DaysLeft(Now, Now.AddDays(31)));
    }
}
