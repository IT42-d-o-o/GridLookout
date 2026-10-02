using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GridLookout.Licensing;

/// <summary>
/// Structural/cryptographic verdict for one licence FILE, independent of trial state - see
/// <see cref="LicenseState"/> for how this composes with <see cref="TrialMarker"/> into the full
/// <see cref="LicenseStatus"/> vocabulary an operator/admin actually sees.
/// </summary>
public enum LicenseFileVerdict
{
    /// <summary>Signature verified, product matches, machine matches (or licence is unbound), not
    /// expired, and this build's date is within the licence's maintenance coverage.</summary>
    Valid,

    /// <summary>Not JSON, missing payload/signature, or either is not valid base64url/JSON.</summary>
    InvalidFormat,

    /// <summary>Well-formed envelope, but the RSA-SHA256 signature does not verify against
    /// <see cref="LicensePublicKey"/>.</summary>
    InvalidSignature,

    /// <summary>Signature verified, but <see cref="LicensePayload.Product"/> is not "GridLookout".</summary>
    WrongProduct,

    /// <summary>Signature verified, product matches, but <see cref="LicensePayload.MachineId"/> is
    /// set and does not match this machine's fingerprint.</summary>
    MachineMismatch,

    /// <summary>Signature/product/machine all check out, but <see cref="LicensePayload.ExpiresAtUtc"/>
    /// is in the past.</summary>
    Expired,

    /// <summary>Signature/product/machine/expiry all check out, but this build's date is after
    /// <see cref="LicensePayload.MaintenanceUntilUtc"/> - a valid, unexpired licence that simply does
    /// not cover a build this new.</summary>
    MaintenanceLapsed,
}

/// <summary>The full result of <see cref="LicenseVerifier.Verify"/> - <see cref="Payload"/> is
/// populated whenever the signature verified (even for <see cref="LicenseFileVerdict.Expired"/>/
/// <see cref="LicenseFileVerdict.MaintenanceLapsed"/>, so callers can still show licence details for
/// a lapsed licence), and null for every format/signature/product/machine failure, where nothing
/// about the payload can be trusted.</summary>
public sealed class LicenseFileResult
{
    public LicenseFileVerdict Verdict { get; init; }

    public LicensePayload? Payload { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>True for every verdict where the signature/product/machine checks all passed - i.e.
    /// this is a genuine, correctly-signed GridLookout licence for this machine, whether or not it is
    /// currently date-gated. Used by <c>--license-install</c> to decide whether to accept a licence
    /// file that happens to be currently expired/maintenance-lapsed (still a real licence, worth
    /// installing) versus one that is outright bogus (never worth installing).</summary>
    public bool IsGenuineLicence =>
        Verdict is LicenseFileVerdict.Valid or LicenseFileVerdict.Expired or LicenseFileVerdict.MaintenanceLapsed;
}

/// <summary>
/// Verifies one gridlookout.lic FILE against <see cref="LicensePublicKey"/> - pure, no file I/O (the
/// caller reads the file text; see <see cref="LicenseState"/> for the file-resolution/trial-fallback
/// orchestration around this). Ported from InfraAtlas's ProductLicenseService.ValidateLicenseText,
/// not referenced - GridLookout is net48, InfraAtlas is net9, and the two have no shared assembly.
/// </summary>
public static class LicenseVerifier
{
    private const string ExpectedProduct = "GridLookout";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <param name="fileContent">The raw UTF-8 text of gridlookout.lic.</param>
    /// <param name="publicKey">Normally <see cref="LicensePublicKey.GetParameters"/> in production;
    /// tests pass their own throwaway keypair's public half instead - see that type's own doc
    /// comment for why parsing the real embedded PEM is a separate, narrower concern from this
    /// method's signature-verification logic.</param>
    /// <param name="nowUtc">The verification clock - injected (not <see cref="DateTime.UtcNow"/>
    /// read directly) so expiry tests are deterministic.</param>
    /// <param name="buildDateUtc">This build's own date (see <c>BuildMetadata.ReadBuildDateUtc</c> in
    /// <see cref="LicenseState"/>) - compared against <see cref="LicensePayload.MaintenanceUntilUtc"/>.</param>
    /// <param name="machineFingerprint">This machine's <see cref="MachineFingerprint.Compute"/> value
    /// - compared against <see cref="LicensePayload.MachineId"/> when the licence sets one.</param>
    public static LicenseFileResult Verify(string fileContent, RSAParameters publicKey, DateTime nowUtc, DateTime buildDateUtc, string machineFingerprint)
    {
        LicenseEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LicenseEnvelope>(fileContent, JsonOptions);
        }
        catch (JsonException)
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence file is not valid JSON.");
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Payload) || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence file is missing 'payload' and/or 'signature'.");
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Base64Url.Decode(envelope.Signature);
        }
        catch (FormatException)
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence 'signature' is not valid base64url.");
        }

        bool signatureOk;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(publicKey);
            var signedBytes = Encoding.UTF8.GetBytes(envelope.Payload);
            try
            {
                signatureOk = rsa.VerifyData(signedBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                signatureOk = false;
            }
        }

        if (!signatureOk)
        {
            return Invalid(LicenseFileVerdict.InvalidSignature, "Licence signature is invalid.");
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.Decode(envelope.Payload);
        }
        catch (FormatException)
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence 'payload' is not valid base64url.");
        }

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(Encoding.UTF8.GetString(payloadBytes), JsonOptions);
        }
        catch (JsonException)
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence payload is not valid JSON.");
        }

        if (payload is null)
        {
            return Invalid(LicenseFileVerdict.InvalidFormat, "Licence payload is empty.");
        }

        if (!string.Equals(payload.Product, ExpectedProduct, StringComparison.Ordinal))
        {
            return new LicenseFileResult
            {
                Verdict = LicenseFileVerdict.WrongProduct,
                Payload = payload,
                Message = $"Licence is for product '{payload.Product}', not {ExpectedProduct}.",
            };
        }

        if (!string.IsNullOrWhiteSpace(payload.MachineId)
            && !string.Equals(payload.MachineId, machineFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseFileResult
            {
                Verdict = LicenseFileVerdict.MachineMismatch,
                Payload = payload,
                Message = $"Licence is bound to a different machine (this machine: {machineFingerprint}).",
            };
        }

        if (payload.ExpiresAtUtc is DateTime expiresAtUtc && expiresAtUtc < nowUtc)
        {
            return new LicenseFileResult
            {
                Verdict = LicenseFileVerdict.Expired,
                Payload = payload,
                Message = $"Licence expired on {expiresAtUtc:yyyy-MM-dd}.",
            };
        }

        if (payload.MaintenanceUntilUtc is DateTime maintenanceUntilUtc && buildDateUtc > maintenanceUntilUtc)
        {
            return new LicenseFileResult
            {
                Verdict = LicenseFileVerdict.MaintenanceLapsed,
                Payload = payload,
                Message = $"This build (dated {buildDateUtc:yyyy-MM-dd}) is newer than the licence's maintenance coverage (ends {maintenanceUntilUtc:yyyy-MM-dd}).",
            };
        }

        return new LicenseFileResult
        {
            Verdict = LicenseFileVerdict.Valid,
            Payload = payload,
            Message = "Licence is valid.",
        };
    }

    private static LicenseFileResult Invalid(LicenseFileVerdict verdict, string message) =>
        new() { Verdict = verdict, Payload = null, Message = message };
}
