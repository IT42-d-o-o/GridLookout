using System;
using System.Text.Json;
using GridLookout.Licensing;
using Xunit;

namespace GridLookout.Tests.Licensing;

public class LicenseVerifierTests : IDisposable
{
    private readonly LicenseTestSigner _signer = new();

    private static readonly DateTime NowUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BuildDateUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string ThisMachine = "AAAAAAAA-BBBBBBBB-CCCCCCCC-DDDDDDDD";

    public void Dispose() => _signer.Dispose();

    private LicenseFileResult VerifyAsIs(string fileContent) =>
        LicenseVerifier.Verify(fileContent, _signer.PublicKey, NowUtc, BuildDateUtc, ThisMachine);

    [Fact]
    public void Verify_WellFormedLicence_IsValid()
    {
        var license = _signer.SignGridLookoutLicense();

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
        Assert.True(result.IsGenuineLicence);
        Assert.NotNull(result.Payload);
        Assert.Equal("GL-2026-000123", result.Payload!.LicenseId);
        Assert.Equal("Acme d.o.o.", result.Payload.CustomerName);
        Assert.Equal("SingleController", result.Payload.Edition);
        Assert.Equal(1, result.Payload.Seats);
        Assert.Equal("perpetual", result.Payload.Term);
    }

    [Fact]
    public void Verify_TamperedSignature_IsInvalidSignature()
    {
        var license = _signer.SignGridLookoutLicense();
        var envelope = JsonSerializer.Deserialize<JsonElement>(license);
        var payload = envelope.GetProperty("payload").GetString();
        var tampered = JsonSerializer.Serialize(new { payload, signature = "aW52YWxpZA" });

        var result = VerifyAsIs(tampered);

        Assert.Equal(LicenseFileVerdict.InvalidSignature, result.Verdict);
        Assert.False(result.IsGenuineLicence);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Verify_PayloadEditedAfterSigning_IsInvalidSignature()
    {
        // Simulates an attacker flipping the payload (e.g. seats/expiry) without re-signing - the
        // signature was computed over the ORIGINAL base64url payload string, so any edit to it,
        // even just re-encoding the SAME logical JSON, invalidates the signature.
        var license = _signer.SignGridLookoutLicense();
        var envelope = JsonSerializer.Deserialize<JsonElement>(license);
        var signature = envelope.GetProperty("signature").GetString();
        var forgedPayload = Base64Url.Encode(System.Text.Encoding.UTF8.GetBytes("{\"product\":\"GridLookout\",\"seats\":999}"));
        var forged = JsonSerializer.Serialize(new { payload = forgedPayload, signature });

        var result = VerifyAsIs(forged);

        Assert.Equal(LicenseFileVerdict.InvalidSignature, result.Verdict);
    }

    [Fact]
    public void Verify_WrongProduct_IsWrongProduct()
    {
        var license = _signer.SignGridLookoutLicense(product: "SomeOtherProduct");

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.WrongProduct, result.Verdict);
        Assert.NotNull(result.Payload);
        Assert.Contains("SomeOtherProduct", result.Message);
    }

    [Fact]
    public void Verify_ExpiredSubscription_IsExpired()
    {
        var license = _signer.SignGridLookoutLicense(term: "subscription", expiresAtUtc: NowUtc.AddDays(-1));

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Expired, result.Verdict);
        Assert.True(result.IsGenuineLicence);
        Assert.NotNull(result.Payload);
    }

    [Fact]
    public void Verify_UnexpiredSubscription_IsValid()
    {
        var license = _signer.SignGridLookoutLicense(term: "subscription", expiresAtUtc: NowUtc.AddDays(30));

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
    }

    [Fact]
    public void Verify_PerpetualLicenceWithNoExpiry_NeverExpires()
    {
        var license = _signer.SignGridLookoutLicense(term: "perpetual", expiresAtUtc: null, maintenanceUntilUtc: NowUtc.AddYears(5));

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
    }

    [Fact]
    public void Verify_BuildDateAfterMaintenanceEnd_IsMaintenanceLapsed()
    {
        var license = _signer.SignGridLookoutLicense(maintenanceUntilUtc: NowUtc.AddDays(-1));

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.MaintenanceLapsed, result.Verdict);
        Assert.True(result.IsGenuineLicence);
        Assert.NotNull(result.Payload);
    }

    [Fact]
    public void Verify_BuildDateCoveredByMaintenance_IsValid()
    {
        var license = _signer.SignGridLookoutLicense(maintenanceUntilUtc: NowUtc.AddDays(1));

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
    }

    [Fact]
    public void Verify_MachineIdMatchesThisMachine_IsValid()
    {
        var license = _signer.SignGridLookoutLicense(machineId: ThisMachine);

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
    }

    [Fact]
    public void Verify_MachineIdMismatch_IsMachineMismatch()
    {
        var license = _signer.SignGridLookoutLicense(machineId: "11111111-22222222-33333333-44444444");

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.MachineMismatch, result.Verdict);
        Assert.NotNull(result.Payload);
        Assert.Contains(ThisMachine, result.Message);
    }

    [Fact]
    public void Verify_NullMachineId_IsFloating_NoNodeLockCheck()
    {
        var license = _signer.SignGridLookoutLicense(machineId: null);

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
    }

    [Fact]
    public void Verify_UnknownFieldsInPayload_AreIgnored()
    {
        var payload = new
        {
            product = "GridLookout",
            licenseId = "GL-2026-000999",
            customerName = "Acme d.o.o.",
            customerEmail = "ops@acme.example",
            edition = "SingleController",
            seats = 1,
            term = "perpetual",
            issuedAtUtc = NowUtc.ToString("O"),
            expiresAtUtc = (string?)null,
            maintenanceUntilUtc = NowUtc.AddYears(1).ToString("O"),
            machineId = (string?)null,
            orderRef = "cs_live_test",
            futureFieldThisBuildDoesNotKnowAbout = "some-new-thing",
            anotherOne = 42,
        };
        var license = _signer.SignPayload(payload);

        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.Valid, result.Verdict);
        Assert.Equal("GL-2026-000999", result.Payload!.LicenseId);
    }

    [Fact]
    public void Verify_NotJson_IsInvalidFormat()
    {
        var result = VerifyAsIs("this is not json at all");

        Assert.Equal(LicenseFileVerdict.InvalidFormat, result.Verdict);
        Assert.False(result.IsGenuineLicence);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Verify_MissingSignatureField_IsInvalidFormat()
    {
        var result = VerifyAsIs(JsonSerializer.Serialize(new { payload = "abc" }));

        Assert.Equal(LicenseFileVerdict.InvalidFormat, result.Verdict);
    }

    [Fact]
    public void Verify_MissingPayloadField_IsInvalidFormat()
    {
        var result = VerifyAsIs(JsonSerializer.Serialize(new { signature = "abc" }));

        Assert.Equal(LicenseFileVerdict.InvalidFormat, result.Verdict);
    }

    [Fact]
    public void Verify_SignatureNotBase64Url_IsInvalidFormat()
    {
        var result = VerifyAsIs(JsonSerializer.Serialize(new { payload = "abc", signature = "not valid base64!!" }));

        Assert.Equal(LicenseFileVerdict.InvalidFormat, result.Verdict);
    }

    [Fact]
    public void Verify_SignedByADifferentKeypair_IsInvalidSignature()
    {
        using var otherSigner = new LicenseTestSigner();
        var license = otherSigner.SignGridLookoutLicense();

        // Verified against THIS test's _signer public key, not otherSigner's - a licence signed by
        // any key other than the one embedded in the running binary must never verify.
        var result = VerifyAsIs(license);

        Assert.Equal(LicenseFileVerdict.InvalidSignature, result.Verdict);
    }
}
