using System;

namespace GridLookout.Licensing;

/// <summary>
/// The on-disk shape of gridlookout.lic - a shared contract with the web-side signer (see
/// docs/plans/2026-09-25-gridlookout-licensing.md "Licence envelope"). UTF-8 JSON:
/// <code>{ "payload": "&lt;base64url payload JSON&gt;", "signature": "&lt;base64url RSA-SHA256 PKCS1v1.5 signature&gt;" }</code>
/// The signature is computed over the UTF-8 BYTES OF THE BASE64URL PAYLOAD STRING itself (exactly
/// as InfraAtlas's ProductLicenseService does), not over the decoded JSON - see
/// <see cref="LicenseVerifier.Verify"/> for the exact bytes signed. Field names are camelCase on the
/// wire; <see cref="LicenseVerifier"/> deserializes with PropertyNameCaseInsensitive so the PascalCase
/// C# properties below still bind. Do not rename these two properties without updating the web
/// brief - the envelope shape is shared, not owned by this repo alone.
/// </summary>
public sealed class LicenseEnvelope
{
    public string Payload { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;
}

/// <summary>
/// The decoded, signed contents of a licence file - see
/// docs/plans/2026-09-25-gridlookout-licensing.md "Licence envelope" for the exact field list and
/// semantics of each. camelCase on the wire (matched case-insensitively - see
/// <see cref="LicenseVerifier"/>); unknown fields are ignored (System.Text.Json's default
/// deserialization behaviour, no extra configuration needed). <see cref="Edition"/> and
/// <see cref="Term"/> are kept as raw strings rather than enums - both are "informational... no
/// enforcement" per the brief, and an unrecognised value here must never fail licence parsing
/// (display it as-is rather than reject the whole file).
/// </summary>
public sealed class LicensePayload
{
    public string Product { get; set; } = string.Empty;

    public string LicenseId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>"SingleController" (seats 1) or "FivePack" (seats 5) - informational only, see the
    /// type's own doc comment.</summary>
    public string Edition { get; set; } = string.Empty;

    /// <summary>Informational in 1.0.2 (logged at startup, shown in --license); no enforcement.</summary>
    public int Seats { get; set; }

    /// <summary>"perpetual" or "subscription" - informational, see the type's own doc comment.</summary>
    public string Term { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; }

    /// <summary>Null for perpetual; for subscription, the period end plus 14 days grace. A non-null
    /// value in the past (relative to the verification clock) is what drives
    /// <see cref="LicenseFileVerdict.Expired"/> in <see cref="LicenseVerifier"/>.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Perpetual = issue + 12 months; subscription = same as <see cref="ExpiresAtUtc"/>. A
    /// build whose OWN build date is after this is not covered by this licence -
    /// <see cref="LicenseFileVerdict.MaintenanceLapsed"/> in <see cref="LicenseVerifier"/>, treated
    /// like Unlicensed by the UI.</summary>
    public DateTime? MaintenanceUntilUtc { get; set; }

    /// <summary>Reserved; when non-null it must equal <see cref="MachineFingerprint.Compute"/> on the
    /// machine verifying the licence (same rule as InfraAtlas's node-lock check) - see
    /// <see cref="LicenseFileVerdict.MachineMismatch"/>. Not issued by the web signer today (null =
    /// licensee-bound, no node-lock), but the field is honoured the moment it IS populated.</summary>
    public string? MachineId { get; set; }

    public string OrderRef { get; set; } = string.Empty;
}
