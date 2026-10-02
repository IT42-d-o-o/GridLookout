using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GridLookout.Logging;

namespace GridLookout.Milestone;

/// <summary>
/// Thin wrapper over the <c>VideoOS.Platform.SDK.Environment</c> login path. <c>AddServer</c> and
/// <c>Login</c> both take a trailing <c>masterOnly</c> bool — <c>true</c> is used here (single-site,
/// non-federated login, this app's only supported topology).
/// </summary>
public sealed class MilestoneSession
{
    // Installed Integration Insights (III) identity — required by the Milestone Technology
    // Partner program. Identifies this integration to the VMS at login; Milestone receives
    // install/usage statistics through XProtect's own telemetry channel, never from this app.
    // The GUID is this product's permanent integration id — do not change it between releases.
    private static readonly Guid IntegrationId = new("0ecb0d3d-006b-4804-8323-fb34af494a0b");
    private const string IntegrationName = "GridLookout";
    private const string ManufacturerName = "IT42";

    private readonly FileLogger? _logger;

    public Uri ServerUri { get; }

    private readonly string _hardwareDecoding;
    private readonly string _decodingThreads;

    public MilestoneSession(Uri serverUri, FileLogger? logger = null,
        string? hardwareDecoding = null, string? decodingThreads = null)
    {
        ServerUri = serverUri;
        _logger = logger;
        _hardwareDecoding = SdkDecodingOptions.NormalizeHardwareDecoding(hardwareDecoding, m => _logger?.Warning(m));
        _decodingThreads = SdkDecodingOptions.NormalizeDecodingThreads(decodingThreads, m => _logger?.Warning(m));
    }

    /// <summary>One-shot init + AddServer + Login. Throws on failure — callers own the retry loop
    /// so they can pump a UI message loop between attempts (see Program.cs).</summary>
    public void Initialize()
    {
        // Issue #12: GridLookout never uses the media toolkit's decoded output (it decodes the JPEG
        // bytes itself), yet the toolkit stands up a decode pipeline per live source, and with an
        // NVIDIA GPU present that is a CUDA context per tile - 1.2 GB private bytes for 20 tiles on
        // the lab wall. The SDK's own EnvironmentOptions knobs (set below, after Initialize - the
        // options dictionary does not exist before it and dereferencing it crashes the process)
        // did not change that figure for JPEGLiveSource, but hiding the GPU from the CUDA runtime
        // did (576 MB, same picture). So HardwareDecoding=Off does both: it hides every CUDA device
        // from THIS process only (process-scoped environment variable, set before the runtime
        // loads) and asks the toolkit for software decoding. Auto leaves the GPU visible.
        if (_hardwareDecoding != "Auto")
        {
            System.Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES", "-1", EnvironmentVariableTarget.Process);
        }

        VideoOS.Platform.SDK.Environment.Initialize();
        // Kept although GridLookout uses no SDK viewer control: measured 2026-09-30 that skipping it
        // changes neither memory nor the per-source Direct3D driver threads (issue #12).
        VideoOS.Platform.SDK.UI.Environment.Initialize();
        // Without this, JPEGLiveSource connects but every LiveContentEvent carries
        // "VideoOS.Platform.SDK.Media.Environment.Initialize() not called" instead of frames.
        VideoOS.Platform.SDK.Media.Environment.Initialize();

        var options = VideoOS.Platform.EnvironmentManager.Instance.EnvironmentOptions;
        options[VideoOS.Platform.EnvironmentOptions.HardwareDecodingMode] = _hardwareDecoding == "Auto"
            ? VideoOS.Platform.EnvironmentOptions.OptionHardwareDecodingModeAuto
            : VideoOS.Platform.EnvironmentOptions.OptionHardwareDecodingModeOff;
        options[VideoOS.Platform.EnvironmentOptions.SoftwareDecodingThreads] = _decodingThreads;
        VideoOS.Platform.EnvironmentManager.Instance.FireEnvironmentOptionsChangedEvent();
        _logger?.Info($"SDK media toolkit: HardwareDecodingMode={_hardwareDecoding} (CUDA devices {(_hardwareDecoding == "Auto" ? "visible" : "hidden")}), SoftwareDecodingThreads={_decodingThreads}.");
    }

    public void Login(CredentialCache credentials)
    {
        // secureOnly follows the configured scheme: forcing it on an http URI would break
        // Windows-auth-over-http setups the config explicitly allows.
        bool secureOnly = string.Equals(ServerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        string version = typeof(MilestoneSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        VideoOS.Platform.SDK.Environment.AddServer(secureOnly, ServerUri, credentials, true);
        VideoOS.Platform.SDK.Environment.Login(ServerUri, IntegrationId, IntegrationName, version, ManufacturerName, true);
        _logger?.Info($"Logged in to {ServerUri} as integration '{IntegrationName}' v{version}");
    }

    /// <summary>
    /// Forces the SDK to re-pull configuration from the Management Server. Without this, the
    /// SDK serves each session a cached configuration and recorder-description edits (the
    /// $layout{} matrix) take minutes to surface — or never, until process restart. Called by
    /// Program.cs's refresh tick right before re-locating the recorder, so description/camera
    /// changes apply within one ConfigRefreshSeconds interval.
    /// </summary>
    public void ReloadConfiguration()
    {
        try
        {
            var roots = VideoOS.Platform.Configuration.Instance.GetItems(VideoOS.Platform.ItemHierarchy.SystemDefined);
            var serverItem = roots?.FirstOrDefault(r => r.FQID.Kind == VideoOS.Platform.Kind.Server);
            if (serverItem is not null)
            {
                VideoOS.Platform.SDK.Environment.ReloadConfiguration(serverItem.FQID);
                // ReloadConfiguration alone does NOT invalidate the cache behind
                // ConfigurationItems.* (RecordingServer.Description stays stale through it) —
                // RefreshConfiguration targets that layer.
                VideoOS.Platform.Configuration.Instance.RefreshConfiguration(
                    serverItem.FQID.ServerId, serverItem.FQID.ObjectId);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"ReloadConfiguration failed (stale config may persist until next relogin): {ex.Message}");
        }
    }

    private static readonly HttpClient RestClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// FIX 3-lite (insecure opt-in): this poll sends the SDK session's own OAuth bearer token in an
    /// <c>Authorization</c> header — refused over plain <c>http://</c> unless the deployment
    /// explicitly opts in, mirroring <see cref="GridLookout.Auth.CredentialFactory.Build"/>'s
    /// identical <c>AllowInsecureBasic</c> gate for <c>AuthMode=Basic</c> login. Pure (no SDK touch)
    /// specifically so it's unit-testable — <see cref="TryGetRecorderDescriptions"/> itself needs a
    /// live SDK session (<c>LoginSettingsCache</c>) and is not. Only gates THIS bearer-token REST
    /// poll — Windows-auth-over-http login (<see cref="GridLookout.Auth.CredentialFactory"/>) is
    /// untouched, by design (a Negotiate handshake never puts a bearer token on the wire the way this
    /// poll's Authorization header does).
    /// </summary>
    public static bool IsLayoutPollAllowed(Uri managementServerUri, bool allowInsecureLayoutPoll) =>
        allowInsecureLayoutPoll || string.Equals(managementServerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Fetches recorder descriptions LIVE via the Management Server REST API
    /// (<c>/API/REST/v1/recordingServers</c>, "array" response shape — same endpoint the
    /// dashboard's HttpMilestoneDataProvider consumes), authenticated with the SDK session's own
    /// OAuth bearer token. Exists because NEITHER SDK.Environment.ReloadConfiguration NOR
    /// Configuration.RefreshConfiguration invalidates the cache behind
    /// ConfigurationItems.RecordingServer.Description: description edits in Management Client
    /// never reach a running session through the SDK, while this REST
    /// read is always current. Returns recorder id→description — FIX 1 (GUID-keyed overlay): keyed
    /// by the recorder's stable Id, never by display name, because two recorders can legitimately
    /// share a name (Management Client does not enforce uniqueness) and a name-keyed dictionary
    /// would collapse them, handing one recorder's Description to both — see
    /// <c>RecorderCatalog.ApplyLiveDescriptions</c>'s own doc comment for the overlay this feeds.
    /// Null when the poll is refused/fails entirely (caller falls back to the SDK-cached
    /// description); an individual array entry with no parseable id is skipped (logged at Debug),
    /// not fatal to the rest of the response.
    /// </summary>
    public Dictionary<Guid, string>? TryGetRecorderDescriptions(bool allowInsecureLayoutPoll)
    {
        // Defense in depth: Program.cs already gates the background poll trigger on this same check
        // (see RecorderCatalog.ShouldPollLiveDescriptions/MilestoneSession.IsLayoutPollAllowed at
        // that call site) so this branch is normally unreachable in production — but this method is
        // public on a public class, so the security property must hold here too, not only be an
        // accident of how Program.cs currently happens to call it.
        if (!IsLayoutPollAllowed(ServerUri, allowInsecureLayoutPoll))
        {
            _logger?.Debug($"REST description poll refused: {ServerUri} is not HTTPS and AllowInsecureLayoutPoll is false.");
            return null;
        }

        try
        {
            var loginSettings = VideoOS.Platform.Login.LoginSettingsCache.GetLoginSettings(ServerUri.Host);
            var token = loginSettings?.IdentityTokenCache?.Token;
            if (string.IsNullOrEmpty(token))
            {
                _logger?.Debug("REST description poll: no OAuth token available on this session.");
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ServerUri, "/API/REST/v1/recordingServers"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = RestClient.SendAsync(request).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("array", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new Dictionary<Guid, string>();
            foreach (var element in array.EnumerateArray())
            {
                // Same fallback chain as the dashboard's HttpMilestoneDataProvider.MapRecorder (the
                // other consumer of this exact endpoint) — different Milestone/XProtect versions have
                // been observed to name the recorder id field differently; mirroring the chain here
                // avoids this poll silently going empty (and the live-lab overlay bug it exists to
                // fix regressing) on a deployment where "id" isn't the field actually present.
                string? idText = element.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                idText ??= element.TryGetProperty("recorderId", out var ridProp) ? ridProp.GetString() : null;
                idText ??= element.TryGetProperty("serverId", out var sidProp) ? sidProp.GetString() : null;
                idText ??= element.TryGetProperty("serverGuid", out var sgProp) ? sgProp.GetString() : null;

                if (idText is null || !Guid.TryParse(idText, out var id))
                {
                    _logger?.Debug($"REST description poll: recordingServers array entry has no parseable id ({(idText is null ? "field absent" : $"'{idText}'")}) — skipped.");
                    continue;
                }

                string description = element.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                result[id] = description;
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger?.Debug($"REST description poll failed (SDK-cached description used): {ex.Message}");
            return null;
        }
    }

    public void Logout()
    {
        try
        {
            VideoOS.Platform.SDK.Environment.Logout(ServerUri);
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Logout error (ignored, shutting down): {ex.Message}");
        }
    }
}
