using System.Net;

namespace Dashboard.Tests;

public class PinnedVersionsTests
{
    private static readonly Uri Address =
        new("https://raw.githubusercontent.com/example-org/demo-system/main/environments/tdd/versions.json");

    private readonly SignallingTimeProvider _time = new();

    private PinnedVersionsReader Reader(StubHandler handler) => new(new HttpClient(handler), _time);

    [Fact]
    public void TheFileIsAnObjectOfVersionsByDeployable()
    {
        var pinned = PinnedVersions.Parse("""
            {
              "dashboard": "1.0.1",
              "ui": "2.4.7"
            }
            """);

        Assert.Equal(PinnedVersionsState.Read, pinned.State);
        Assert.Null(pinned.Detail);
        Assert.Equal("2.4.7", pinned.Of("ui"));
        Assert.Equal("1.0.1", pinned.Of("dashboard"));
        Assert.Null(pinned.Of("api"));
        Assert.Equal(2, pinned.Versions.Count);
    }

    [Fact]
    public void BuildMetadataIsLeftOut() =>
        Assert.Equal("2.4.7", PinnedVersions.Parse("""{ "ui": "2.4.7+0a1b2c3" }""").Of("ui"));

    [Fact]
    public void AnEmptyObjectIsReadAndPinsNothing()
    {
        // The file a new environment starts with.
        var pinned = PinnedVersions.Parse("{}\n");

        Assert.Equal(PinnedVersionsState.Read, pinned.State);
        Assert.Empty(pinned.Versions);
        Assert.Null(pinned.Of("ui"));
    }

    [Fact]
    public void AnEntryThatIsNotAVersionIsNoEntry()
    {
        var pinned = PinnedVersions.Parse("""{ "ui": 2, "api": null, "worker": "", "job": { "version": "1.0.0" }, "web": "3.1.0" }""");

        Assert.Equal(PinnedVersionsState.Read, pinned.State);
        Assert.Equal(["web"], pinned.Versions.Keys);
    }

    [Theory]
    [InlineData(null, "the file is empty")]
    [InlineData("", "the file is empty")]
    [InlineData("  \n", "the file is empty")]
    [InlineData("<!DOCTYPE html><html></html>", "the file is not valid JSON")]
    [InlineData("""{ "ui": "2.4.7", """, "the file is not valid JSON")]
    [InlineData("404: Not Found", "the file is not valid JSON")]
    [InlineData("[]", "the file does not contain a JSON object")]
    [InlineData("\"2.4.7\"", "the file does not contain a JSON object")]
    public void AMalformedFileIsUnavailableWithTheReason(string? json, string reason)
    {
        var pinned = PinnedVersions.Parse(json);

        Assert.Equal(PinnedVersionsState.Unavailable, pinned.State);
        Assert.Equal(reason, pinned.Detail);
        Assert.Empty(pinned.Versions);
        Assert.Null(pinned.Of("ui"));
    }

    [Fact]
    public void TheStatesWithoutAFilePinNothing()
    {
        Assert.Equal(PinnedVersionsState.NotTracked, PinnedVersions.NotTracked.State);
        Assert.Equal(PinnedVersionsState.Pending, PinnedVersions.Pending.State);
        Assert.Equal(PinnedVersionsState.Missing, PinnedVersions.Missing.State);
        Assert.All(
            [PinnedVersions.NotTracked, PinnedVersions.Pending, PinnedVersions.Missing],
            pinned => Assert.Null(pinned.Of("ui")));
    }

    [Fact]
    public async Task TheFileIsReadWithOneGetWithoutTheBrowserCache()
    {
        var handler = new StubHandler(_ => StubHandler.Answer(HttpStatusCode.OK, """{ "ui": "2.4.7" }"""));

        var pinned = await Reader(handler).ReadAsync(Address, CancellationToken.None);

        Assert.Equal(PinnedVersionsState.Read, pinned.State);
        Assert.Equal("2.4.7", pinned.Of("ui"));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Address, request.RequestUri);
        Assert.Equal("no-store", NodeProberTests.FetchOption(request, "cache"));

        // A request without headers of its own needs no CORS preflight.
        Assert.Empty(request.Headers);
    }

    [Fact]
    public async Task AFileThatDoesNotExistIsMissing()
    {
        var handler = new StubHandler(_ => StubHandler.Answer(HttpStatusCode.NotFound, "404: Not Found"));

        var pinned = await Reader(handler).ReadAsync(Address, CancellationToken.None);

        Assert.Equal(PinnedVersionsState.Missing, pinned.State);
        Assert.Empty(pinned.Versions);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "the server answered HTTP 500")]
    [InlineData(HttpStatusCode.TooManyRequests, "the server answered HTTP 429")]
    [InlineData(HttpStatusCode.Forbidden, "the server answered HTTP 403")]
    public async Task AnyOtherFailingStatusIsUnavailableWithTheStatus(HttpStatusCode status, string reason)
    {
        var handler = new StubHandler(_ => StubHandler.Answer(status, """{ "ui": "2.4.7" }"""));

        var pinned = await Reader(handler).ReadAsync(Address, CancellationToken.None);

        Assert.Equal(PinnedVersionsState.Unavailable, pinned.State);
        Assert.Equal(reason, pinned.Detail);
        Assert.Null(pinned.Of("ui"));
    }

    [Fact]
    public async Task ANetworkOrCorsFailureIsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("TypeError: Failed to fetch"));

        var pinned = await Reader(handler).ReadAsync(Address, CancellationToken.None);

        Assert.Equal(PinnedVersionsState.Unavailable, pinned.State);
        Assert.Contains("could not read an answer", pinned.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedAnswerIsUnavailable()
    {
        var handler = new StubHandler(_ => StubHandler.Answer(HttpStatusCode.OK, "<!DOCTYPE html><html></html>"));

        var pinned = await Reader(handler).ReadAsync(Address, CancellationToken.None);

        Assert.Equal(PinnedVersionsState.Unavailable, pinned.State);
        Assert.Equal("the file is not valid JSON", pinned.Detail);
    }

    [Fact]
    public async Task NoAnswerWithinTheTimeoutIsUnavailable()
    {
        var handler = new StubHandler((_, cancellationToken) => StubHandler.NeverAsync(cancellationToken));

        var reading = Reader(handler).ReadAsync(Address, CancellationToken.None);
        _time.Advance(NodeProber.DefaultTimeout);
        var pinned = await reading.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(PinnedVersionsState.Unavailable, pinned.State);
        Assert.Equal("no answer within 10 s", pinned.Detail);
    }

    [Fact]
    public async Task TheCallersCancellationIsNotAResult()
    {
        var handler = new StubHandler((_, cancellationToken) => StubHandler.NeverAsync(cancellationToken));
        using var stopping = new CancellationTokenSource();

        var reading = Reader(handler).ReadAsync(Address, stopping.Token);
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
