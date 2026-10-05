using System.Net;
using System.Text.Json;

namespace Dashboard.Health;

public enum PinnedVersionsState
{
    /// <summary>The topology names no <c>versions.json</c> for the environment: versions are not compared.</summary>
    NotTracked,

    /// <summary>The file has not been read yet.</summary>
    Pending,

    /// <summary>The file was read.</summary>
    Read,

    /// <summary>
    /// The file was not found (HTTP 404): nothing has been deployed to the environment, or the repository is not public.
    /// </summary>
    Missing,

    /// <summary>The file could not be read: no answer, another HTTP status, or content that is not a JSON object.</summary>
    Unavailable,
}

/// <summary>
/// The versions the deployments pinned in Git for one environment: the content of the system repository's
/// <c>environments/&lt;environment&gt;/versions.json</c>, a JSON object <c>{ "&lt;deployable&gt;": "&lt;version&gt;" }</c>.
/// </summary>
/// <param name="State">Whether the file was read.</param>
/// <param name="Versions">The version by deployable name, without build metadata; empty unless the file was read.</param>
/// <param name="Detail">Why the file could not be read, as the end of a sentence.</param>
public sealed record PinnedVersions(PinnedVersionsState State, IReadOnlyDictionary<string, string> Versions, string? Detail = null)
{
    public const string FileName = "versions.json";

    private static readonly Dictionary<string, string> None = new(StringComparer.Ordinal);

    public static PinnedVersions NotTracked { get; } = new(PinnedVersionsState.NotTracked, None);

    public static PinnedVersions Pending { get; } = new(PinnedVersionsState.Pending, None);

    public static PinnedVersions Missing { get; } = new(PinnedVersionsState.Missing, None);

    public static PinnedVersions Unavailable(string detail) => new(PinnedVersionsState.Unavailable, None, detail);

    /// <summary>The version pinned for a deployable; null when the file was not read or has no entry for it.</summary>
    public string? Of(string deployable) => Versions.GetValueOrDefault(deployable);

    /// <summary>
    /// Reads the content of <c>versions.json</c>. An entry whose value is not a version (not text, or empty) is no
    /// entry: the deployable then counts as not deployed.
    /// </summary>
    public static PinnedVersions Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Unavailable("the file is empty");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Unavailable("the file does not contain a JSON object");
            }

            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && VersionText.Display(property.Value.GetString()) is { } version)
                {
                    versions[property.Name] = version;
                }
            }

            return new PinnedVersions(PinnedVersionsState.Read, versions);
        }
        catch (JsonException)
        {
            return Unavailable("the file is not valid JSON");
        }
    }
}

/// <summary>Reads an environment's <c>versions.json</c> from the address the topology gives.</summary>
public sealed class PinnedVersionsReader(HttpClient http, TimeProvider time)
{
    /// <summary>How long the answer may take: as long as a node's.</summary>
    public TimeSpan Timeout { get; init; } = NodeProber.DefaultTimeout;

    /// <summary>
    /// Never throws for a file that cannot be read: that is the result Missing or Unavailable. Only the caller's
    /// cancellation throws.
    /// </summary>
    public async Task<PinnedVersions> ReadAsync(Uri address, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = NodeProber.NewRequest(address);
            using var response = await http.SendAsync(request, linked.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return PinnedVersions.Missing;
            }

            return response.IsSuccessStatusCode
                ? PinnedVersions.Parse(await response.Content.ReadAsStringAsync(linked.Token))
                : PinnedVersions.Unavailable($"the server answered HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PinnedVersions.Unavailable($"no answer within {Timeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException)
        {
            return PinnedVersions.Unavailable("the browser could not read an answer (network or CORS)");
        }
    }
}
