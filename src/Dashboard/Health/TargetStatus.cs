namespace Dashboard.Health;

public enum TargetKind
{
    /// <summary>The public address of a deployable in an environment.</summary>
    FrontDoor,

    /// <summary>One regional web app behind the public address.</summary>
    Node,
}

/// <summary>One tile of the dashboard: an endpoint, its last check and the checks before it.</summary>
public sealed class TargetStatus(TargetKind kind, string name, Uri url, string? region = null, string? role = null)
{
    public const int HistoryLength = 30;

    public TargetKind Kind { get; } = kind;

    public string Name { get; } = name;

    public Uri Url { get; } = url;

    public string? Region { get; } = region;

    public string? Role { get; } = role;

    public bool IsPrimary => string.Equals(Role, NodeInfo.PrimaryRole, StringComparison.OrdinalIgnoreCase);

    /// <summary>The last <see cref="HistoryLength"/> checks, oldest first.</summary>
    public HistoryBuffer<ProbeResult> History { get; } = new(HistoryLength);

    public ProbeResult? Last { get; private set; }

    public HealthState State => Last?.State ?? HealthState.Pending;

    /// <summary>The version the endpoint last reported: a node that stops answering keeps its last known version.</summary>
    public string? Version { get; private set; }

    /// <summary>The node's own count of its calls at the last check; null when it reported none.</summary>
    public TelemetrySnapshot? Telemetry { get; private set; }

    public void RecordTelemetry(TelemetrySnapshot? telemetry) => Telemetry = telemetry;

    public void Record(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Last = result;
        Version = result.Version ?? Version;
        History.Add(result);
    }

    public NodeHealth ToNodeHealth() => new(Name, Region, IsPrimary, State);

    public NodeVersion ToNodeVersion() => new(Region ?? Name, State, Version);
}

/// <summary>A deployable of an environment with the state of its endpoints.</summary>
public sealed class DeployableStatus
{
    public DeployableStatus(DeployableInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        FrontDoor = info.FrontDoor is null ? null : new TargetStatus(TargetKind.FrontDoor, "Front Door", info.FrontDoor);
        Nodes = [.. info.Nodes.Select(node => new TargetStatus(TargetKind.Node, node.Name, node.Url, node.Region, node.Role))];
    }

    public DeployableInfo Info { get; }

    public TargetStatus? FrontDoor { get; }

    public IReadOnlyList<TargetStatus> Nodes { get; }

    /// <summary>The Front Door endpoint first, then the nodes in the order of the topology.</summary>
    public IEnumerable<TargetStatus> Targets => FrontDoor is null ? Nodes : Nodes.Prepend(FrontDoor);

    public ServingAssessment Assess() => Assess(out _);

    /// <summary>The serving decision, and the tile of the node it expects to serve the traffic (if any).</summary>
    public ServingAssessment Assess(out TargetStatus? expected)
    {
        var healths = Nodes.Select(node => node.ToNodeHealth()).ToList();
        var assessment = ServingAssessment.Assess(healths, FrontDoor?.State);
        var index = healths.FindIndex(health => ReferenceEquals(health, assessment.Expected));
        expected = index >= 0 ? Nodes[index] : null;
        return assessment;
    }
}

/// <summary>An environment with the state of its deployables and the versions the deployments pinned for it in Git.</summary>
public sealed class EnvironmentStatus
{
    public EnvironmentStatus(EnvironmentInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        Deployables = [.. info.Deployables.Select(deployable => new DeployableStatus(deployable))];
        Pinned = info.VersionsUrl is null ? PinnedVersions.NotTracked : PinnedVersions.Pending;
    }

    public EnvironmentInfo Info { get; }

    public string Name => Info.Name;

    public string? Tier => Info.Tier;

    public IReadOnlyList<DeployableStatus> Deployables { get; }

    /// <summary>The last reading of the environment's <c>versions.json</c>; a failed reading replaces a good one.</summary>
    public PinnedVersions Pinned { get; private set; }

    /// <summary>True when a deployable of this environment has a node that runs another version than the pinned one.</summary>
    public bool VersionsDiffer => Deployables.Any(deployable => AssessVersions(deployable) is { Differs: true });

    public void Record(PinnedVersions pinned)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        Pinned = pinned;
    }

    /// <summary>
    /// The pinned version of a deployable next to the versions its nodes run; null when the topology names no
    /// <c>versions.json</c> for this environment.
    /// </summary>
    public VersionAssessment? AssessVersions(DeployableStatus deployable)
    {
        ArgumentNullException.ThrowIfNull(deployable);
        return Info.VersionsUrl is null
            ? null
            : VersionAssessment.Assess(Pinned, deployable.Info.Name, deployable.Nodes.Select(node => node.ToNodeVersion()));
    }
}
