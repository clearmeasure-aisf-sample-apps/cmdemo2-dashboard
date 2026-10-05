using System.Globalization;
using Dashboard.Health;

namespace Dashboard.Runtime;

/// <summary>
/// The runtime view's update from the monitor's current state: the same checks, serving decision and version
/// comparison the health view shows, mapped onto the diagram's elements by the manifest. A node of the manifest is
/// matched with a target of the monitor by its address (web app, Front Door endpoint), within the environment of the
/// same name; a node the monitor does not check is drawn neutral, with the reason in words.
/// </summary>
public static class RuntimePayloadBuilder
{
    public const string Healthy = "healthy";
    public const string Unhealthy = "unhealthy";
    public const string Unreachable = "unreachable";
    public const string Checking = "checking";
    public const string Neutral = "neutral";

    /// <summary>The number line's placeholder until a source of calls per minute exists.</summary>
    public const string NoNumber = "–";
    public const string CallsUnit = "calls/min";

    private sealed record Entry(DeployableStatus Deployable, TargetStatus Target, ServingAssessment Assessment, TargetStatus? Expected);

    /// <param name="manifest">The environment's manifest.</param>
    /// <param name="environment">The monitor's environment of the same name; null when the topology has none.</param>
    /// <param name="page">The dashboard's own address: the static site that serves it is "this page".</param>
    /// <param name="zone">The viewer's time zone, for the tooltips.</param>
    public static RuntimePayload Build(RuntimeManifest manifest, EnvironmentStatus? environment, Uri? page, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(zone);
        var entries = Index(environment);
        var byAlias = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var node in manifest.Nodes)
        {
            if (Find(entries, node) is { } entry)
            {
                byAlias[node.Alias] = entry;
            }
        }

        var tiles = manifest.Nodes
            .Where(node => node.Kind != RuntimeNodeKind.Person)
            .Select(node => Tile(node, byAlias.GetValueOrDefault(node.Alias), environment, page, zone))
            .ToList();
        var regions = manifest.Regions.Select(region => Region(region, manifest, byAlias)).ToList();
        var edges = manifest.Edges.Select(edge => Edge(edge, byAlias)).ToList();
        return new RuntimePayload(tiles, regions, edges);
    }

    /// <summary>The state word of a health state, as the payload carries it.</summary>
    public static string StateOf(HealthState state) => state switch
    {
        HealthState.Healthy => Healthy,
        HealthState.Unhealthy => Unhealthy,
        HealthState.Unreachable => Unreachable,
        _ => Checking,
    };

    private static List<Entry> Index(EnvironmentStatus? environment)
    {
        var entries = new List<Entry>();
        if (environment is null)
        {
            return entries;
        }

        foreach (var deployable in environment.Deployables)
        {
            var assessment = deployable.Assess(out var expected);
            entries.AddRange(deployable.Targets.Select(target => new Entry(deployable, target, assessment, expected)));
        }

        return entries;
    }

    private static Entry? Find(List<Entry> entries, RuntimeNode node)
    {
        var kind = node.Kind switch
        {
            RuntimeNodeKind.WebApp => TargetKind.Node,
            RuntimeNodeKind.FrontDoor => TargetKind.FrontDoor,
            _ => (TargetKind?)null,
        };
        return kind is null || node.Url is null
            ? null
            : entries.FirstOrDefault(entry => entry.Target.Kind == kind && entry.Target.Url == node.Url);
    }

    private static RuntimeTile Tile(RuntimeNode node, Entry? entry, EnvironmentStatus? environment, Uri? page, TimeZoneInfo zone)
    {
        if (entry is not null)
        {
            return node.Kind == RuntimeNodeKind.FrontDoor ? FrontDoorTile(node, entry, zone) : WebAppTile(node, entry, environment!, zone);
        }

        return node.Kind switch
        {
            RuntimeNodeKind.Sql => NeutralTile(
                node,
                "Not probed",
                "not probed from the browser",
                $"{node.Name}: Azure SQL takes no call from a browser. The health check of each web app connects to it (probe: Health check)."),
            RuntimeNodeKind.StaticSite when node.Url is not null && page is not null && SameSite(node.Url, page) => NeutralTile(
                node,
                "This page",
                "serves this dashboard",
                $"{node.Name} ({node.Url.Host}) serves the page you are looking at."),
            RuntimeNodeKind.StaticSite => NeutralTile(
                node,
                "Not probed",
                node.Url is null ? "its address is not in this deployment" : node.Url.Host,
                $"{node.Name}: the dashboard of this environment. It does not check itself."),
            RuntimeNodeKind.FrontDoor when node.Url is null => NeutralTile(
                node,
                "No address",
                "endpoint not deployed yet",
                $"{node.Name}: the deployment found no such endpoint in the Front Door profile. Deploy the dashboard again once the environment has it."),
            RuntimeNodeKind.WebApp or RuntimeNodeKind.FrontDoor => NeutralTile(
                node,
                "Not checked",
                "not in topology.json",
                $"{node.Name}: the topology this page loaded has no such address, so it is not checked. Reload the topology, or deploy the dashboard again."),
            _ => NeutralTile(node, "Not probed", string.Empty, node.Name),
        };
    }

    private static RuntimeTile NeutralTile(RuntimeNode node, string label, string line, string title) =>
        new(node.Alias, Neutral, label, null, line.Length > 0 ? [new RuntimeTileLine(line, "muted")] : [], null, title);

    private static RuntimeTile WebAppTile(RuntimeNode node, Entry entry, EnvironmentStatus environment, TimeZoneInfo zone)
    {
        var target = entry.Target;
        var lines = new List<RuntimeTileLine> { VersionLine(target) };
        if (PinnedLine(environment.AssessVersions(entry.Deployable), target) is { } pinned)
        {
            lines.Add(pinned);
        }

        var role = target.Role ?? (target.IsPrimary ? NodeInfo.PrimaryRole : NodeInfo.StandbyRole);
        lines.Add(ReferenceEquals(target, entry.Expected)
            ? new RuntimeTileLine($"{role}: serves traffic", "serving")
            : target.State switch
            {
                HealthState.Healthy => new RuntimeTileLine($"{role}: ready, no traffic", "muted"),
                HealthState.Pending => new RuntimeTileLine(role, "muted"),
                _ => new RuntimeTileLine($"{role}: not serving", "plain"),
            });
        return Checked(node, target, lines, zone);
    }

    private static RuntimeTile FrontDoorTile(RuntimeNode node, Entry entry, TimeZoneInfo zone)
    {
        var assessment = entry.Assessment;
        var lines = new List<RuntimeTileLine> { VersionLine(entry.Target) };
        lines.Add(assessment.State switch
        {
            ServingState.Primary => new RuntimeTileLine($"routes to {assessment.Expected!.Label} (priority 1)", "plain"),
            ServingState.FailedOver => new RuntimeTileLine($"routes to {assessment.Expected!.Label} (failed over)", "serving"),
            ServingState.Down => new RuntimeTileLine("no healthy origin", "plain"),
            ServingState.NoNodes => new RuntimeTileLine("no origins in the topology", "muted"),
            _ => new RuntimeTileLine("origins being checked", "muted"),
        });
        lines.Add(assessment.FrontDoor switch
        {
            FrontDoorAgreement.Agrees => new RuntimeTileLine("agrees with the web apps", "ok"),
            FrontDoorAgreement.Disagrees => new RuntimeTileLine("disagrees with the web apps", "warn"),
            _ => new RuntimeTileLine("being compared with the web apps", "muted"),
        });
        return Checked(node, entry.Target, lines, zone, assessment.FrontDoorText);
    }

    private static RuntimeTile Checked(RuntimeNode node, TargetStatus target, List<RuntimeTileLine> lines, TimeZoneInfo zone, string? note = null)
    {
        var last = target.Last;
        var facts = last switch
        {
            null => "not checked yet",
            { StatusCode: { } status, LatencyMs: { } latency } => string.Create(CultureInfo.InvariantCulture, $"HTTP {status} · {latency} ms"),
            { StatusCode: { } status } => string.Create(CultureInfo.InvariantCulture, $"HTTP {status}"),
            _ => "no answer",
        };
        var title = new List<string> { $"{node.Name}: {HealthClassifier.Label(target.State)}", target.Url.AbsoluteUri };
        if (last is not null)
        {
            title.Add($"Last check {TimeText.Clock(last.CheckedAt, zone)}{(last.Detail is { } detail ? $": {detail}" : string.Empty)}");
        }

        title.Add(HistoryText.Describe(target.History));
        if (note is not null)
        {
            title.Add(note);
        }

        return new RuntimeTile(
            node.Alias,
            StateOf(target.State),
            HealthClassifier.Label(target.State),
            facts,
            lines,
            [.. target.History.Select(result => StateOf(result.State))],
            string.Join('\n', title));
    }

    private static RuntimeTileLine VersionLine(TargetStatus target) =>
        target.Version is { } version ? new RuntimeTileLine($"version {version}", "strong") : new RuntimeTileLine("version not known", "muted");

    /// <summary>This node's part of the comparison with the version pinned in Git; null when nothing is pinned for the environment.</summary>
    private static RuntimeTileLine? PinnedLine(VersionAssessment? versions, TargetStatus target)
    {
        if (versions is null)
        {
            return null;
        }

        var label = target.Region ?? target.Name;
        bool Has(IReadOnlyList<NodeVersion> nodes) => nodes.Any(node => node.Label == label);
        return versions.State switch
        {
            VersionState.Pending => new RuntimeTileLine("reading the pinned version", "unknown"),
            VersionState.NotDeployed => new RuntimeTileLine("no pinned version", "unknown"),
            VersionState.PinnedUnknown => new RuntimeTileLine("pinned version not known", "unknown"),
            _ when Has(versions.Differing) => new RuntimeTileLine($"differs from pinned {versions.Pinned}", "differs"),
            _ when Has(versions.Matching) => new RuntimeTileLine($"pinned {versions.Pinned}: in sync", "insync"),
            _ => new RuntimeTileLine($"pinned {versions.Pinned}: not compared", "unknown"),
        };
    }

    private static RuntimeRegionMark Region(RuntimeRegion region, RuntimeManifest manifest, Dictionary<string, Entry> byAlias)
    {
        var apps = manifest.Nodes
            .Where(node => node.Kind == RuntimeNodeKind.WebApp && node.RegionAlias == region.Alias)
            .Select(node => byAlias.GetValueOrDefault(node.Alias))
            .ToList();
        if (apps.Count == 0)
        {
            var roles = region.Roles.Select(role => role switch
            {
                "data" => "database",
                "static" => "static sites",
                _ => role,
            });
            return new RuntimeRegionMark(region.Alias, Neutral, $"{string.Join(", ", roles)}: not probed");
        }

        // A web app the topology does not have is left out; a region with none the topology has is not checked.
        var known = apps.OfType<Entry>().ToList();
        return known.Count == 0 ? new RuntimeRegionMark(region.Alias, Neutral, "not checked") : Region(region.Alias, known);
    }

    private static RuntimeRegionMark Region(string alias, List<Entry> apps)
    {
        if (apps.Any(entry => ReferenceEquals(entry.Target, entry.Expected)))
        {
            return new RuntimeRegionMark(alias, "serving", "serving traffic");
        }

        if (apps.All(entry => entry.Target.State == HealthState.Healthy))
        {
            return new RuntimeRegionMark(alias, "standby", "standby: ready");
        }

        return apps.Any(entry => entry.Target.State == HealthState.Pending)
            ? new RuntimeRegionMark(alias, Checking, "checking")
            : new RuntimeRegionMark(alias, "down", "not serving");
    }

    private static RuntimeEdgeMark Edge(RuntimeEdge edge, Dictionary<string, Entry> byAlias)
    {
        var from = byAlias.GetValueOrDefault(edge.From);
        var to = byAlias.GetValueOrDefault(edge.To);
        switch (edge.Kind)
        {
            case RuntimeEdgeKind.Origin:
            {
                var role = edge.Priority == 1
                    ? "first, while healthy"
                    : "when priority 1 is down";
                var state = to is null ? Neutral : Carries(to);
                return new RuntimeEdgeMark(edge.Id, state, NoNumber, CallsUnit, role, $"Front Door to {edge.To}, origin priority {edge.Priority?.ToString(CultureInfo.InvariantCulture) ?? "not known"}: {role}. {Words(state)} Calls per minute: not measured yet.");
            }

            case RuntimeEdgeKind.Sql:
            {
                // A web app that is down sends no queries: the database is not the reason, so the line is idle.
                var carries = from is null ? Neutral : Carries(from);
                var state = carries == "down" ? "idle" : carries;
                return new RuntimeEdgeMark(edge.Id, state, NoNumber, CallsUnit, "queries of the app", $"{edge.From} to the database. {Words(state)} Calls per minute: not measured yet.");
            }

            case RuntimeEdgeKind.Public:
            {
                var target = to ?? from;
                var state = target is null ? Neutral : target.Target.State switch
                {
                    HealthState.Healthy => "active",
                    HealthState.Pending => Checking,
                    _ => "down",
                };
                return new RuntimeEdgeMark(edge.Id, state, null, null, null, $"The browser to {edge.To}: {Words(state)}");
            }

            default:
                return new RuntimeEdgeMark(edge.Id, Neutral, null, null, null, $"{edge.From} to {edge.To}");
        }
    }

    /// <summary>Whether the traffic of a node's deployable goes through this node, by the serving decision.</summary>
    private static string Carries(Entry entry) =>
        ReferenceEquals(entry.Target, entry.Expected) ? "active"
        : entry.Target.State == HealthState.Healthy ? "idle"
        : entry.Target.State == HealthState.Pending ? Checking
        : "down";

    private static string Words(string state) => state switch
    {
        "active" => "Carries the traffic.",
        "idle" => "Idle: no traffic expected.",
        "down" => "Down: its end is not healthy.",
        Checking => "Being checked.",
        _ => "Not checked.",
    };

    private static bool SameSite(Uri site, Uri page) =>
        string.Equals(site.Host, page.Host, StringComparison.OrdinalIgnoreCase) && site.Port == page.Port;
}
