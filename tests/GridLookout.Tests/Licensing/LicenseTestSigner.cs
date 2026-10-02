using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GridLookout.Licensing;

namespace GridLookout.Tests.Licensing;

/// <summary>
/// Test-only signer mirroring the web-side signer's algorithm exactly (RSA-SHA256 PKCS1v1.5 over the
/// UTF-8 bytes of the base64url payload string) - see LicenseEnvelope's own doc comment for the
/// contract this must match. Generates its own throwaway RSA keypair; NEVER touches the real
/// embedded LicensePublicKey/Vault private key, per this task's contract.
/// </summary>
internal sealed class LicenseTestSigner : IDisposable
{
    public RSA Rsa { get; }

    public RSAParameters PublicKey { get; }

    public LicenseTestSigner(int keySizeBits = 2048)
    {
        Rsa = RSA.Create(keySizeBits);
        PublicKey = Rsa.ExportParameters(includePrivateParameters: false);
    }

    public void Dispose() => Rsa.Dispose();

    /// <summary>Builds a signed envelope JSON string from an already-populated payload object (a
    /// Dictionary/anonymous object serialised as-is, so a test can omit or add fields freely to
    /// exercise "unknown fields ignored" and format-error paths).</summary>
    public string SignPayload(object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadB64 = Base64Url.Encode(Encoding.UTF8.GetBytes(payloadJson));
        var signatureBytes = Rsa.SignData(Encoding.UTF8.GetBytes(payloadB64), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var envelope = new { payload = payloadB64, signature = Base64Url.Encode(signatureBytes) };
        return JsonSerializer.Serialize(envelope);
    }

    /// <summary>Convenience overload for the common case: a well-formed GridLookout payload with
    /// sensible defaults, overridable per test.</summary>
    public string SignGridLookoutLicense(
        string licenseId = "GL-2026-000123",
        string customerName = "Acme d.o.o.",
        string edition = "SingleController",
        int seats = 1,
        string term = "perpetual",
        DateTime? issuedAtUtc = null,
        DateTime? expiresAtUtc = null,
        DateTime? maintenanceUntilUtc = null,
        string? machineId = null,
        string product = "GridLookout")
    {
        var issued = issuedAtUtc ?? new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var payload = new
        {
            product,
            licenseId,
            customerName,
            customerEmail = "ops@acme.example",
            edition,
            seats,
            term,
            issuedAtUtc = issued.ToString("O"),
            expiresAtUtc = expiresAtUtc?.ToString("O"),
            maintenanceUntilUtc = (maintenanceUntilUtc ?? issued.AddMonths(12)).ToString("O"),
            machineId,
            orderRef = "cs_live_test",
        };

        return SignPayload(payload);
    }
}
