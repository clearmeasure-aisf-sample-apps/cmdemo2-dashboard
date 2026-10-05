using System.Text.Json;

namespace Dashboard.Health;

/// <summary>
/// A node's own count of its calls over the last minute, as its telemetry endpoint (<c>/_telemetry</c>) reports them:
/// the counters the app keeps in its process, so the numbers are live and cost nothing outside it.
/// </summary>
/// <param name="Requests">Traffic: requests that are not checks, per minute.</param>
/// <param name="FromFrontDoor">Of those, the ones Front Door forwarded (header <c>X-Azure-FDID</c>).</param>
/// <param name="Direct">Of those, the ones to the web app's own address.</param>
/// <param name="Errors">Traffic answered with HTTP 500 or more.</param>
/// <param name="P95Ms">The 95th percentile of the traffic's duration, in milliseconds; null without traffic.</param>
/// <param name="Probes">The checks of dashboards and diagnostics (paths such as <c>/_healthcheck</c>).</param>
/// <param name="FrontDoorProbes">Front Door's health probes of this origin.</param>
/// <param name="Sql">SQL commands the process ran, whatever caused them.</param>
/// <param name="SqlP95Ms">The 95th percentile of their duration, in milliseconds; null without one.</param>
/// <param name="Http">Outgoing HTTP calls.</param>
/// <param name="ReadAt">When the dashboard read it.</param>
public sealed record TelemetrySnapshot(
    int Requests,
    int FromFrontDoor,
    int Direct,
    int Errors,
    int? P95Ms,
    int Probes,
    int FrontDoorProbes,
    int Sql,
    int? SqlP95Ms,
    int Http,
    DateTimeOffset ReadAt)
{
    /// <summary>The endpoint's answer; null when it is not the expected JSON (an older app, an error page).</summary>
    public static TelemetrySnapshot? Parse(string json, DateTimeOffset readAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("requests", out var requests) || requests.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var probes = Section(root, "probes");
            var sql = Section(root, "sql");
            var http = Section(root, "http");
            return new TelemetrySnapshot(
                Count(requests, "perMinute"),
                Count(requests, "frontDoor"),
                Count(requests, "direct"),
                Count(requests, "errors"),
                Optional(requests, "p95Ms"),
                Count(probes, "perMinute"),
                Count(probes, "frontDoor"),
                Count(sql, "perMinute"),
                Optional(sql, "p95Ms"),
                Count(http, "perMinute"),
                readAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Section(JsonElement root, string name) =>
        root.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object ? section : null;

    private static int Count(JsonElement? section, string name) => Optional(section, name) ?? 0;

    private static int? Optional(JsonElement? section, string name) =>
        section is { } value && value.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number
            && number.TryGetDouble(out var real) && real >= 0
            ? (int)Math.Round(real)
            : null;
}
