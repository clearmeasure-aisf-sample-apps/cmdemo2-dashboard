namespace Dashboard.Tests;

public class ViewAddressTests
{
    [Theory]
    [InlineData(null, DashboardView.Health, null)]
    [InlineData("", DashboardView.Health, null)]
    [InlineData("#", DashboardView.Health, null)]
    [InlineData("health", DashboardView.Health, null)]
    [InlineData("env-0", DashboardView.Health, null)]
    [InlineData("runtime", DashboardView.Runtime, null)]
    [InlineData("#runtime", DashboardView.Runtime, null)]
    [InlineData("Runtime/", DashboardView.Runtime, null)]
    [InlineData("runtime/uat", DashboardView.Runtime, "uat")]
    [InlineData("#runtime/prod/", DashboardView.Runtime, "prod")]
    public void TheHashNamesTheView(string? hash, DashboardView view, string? environment)
    {
        Assert.Equal(new ViewAddress(view, environment), ViewAddress.Parse(hash));
    }

    [Fact]
    public void AViewWritesTheHashThatOpensIt()
    {
        Assert.Equal(string.Empty, ViewAddress.Health.ToHash());
        Assert.Equal("runtime", new ViewAddress(DashboardView.Runtime).ToHash());
        Assert.Equal("runtime/uat", new ViewAddress(DashboardView.Runtime, "uat").ToHash());
        Assert.Equal(new ViewAddress(DashboardView.Runtime, "uat"), ViewAddress.Parse(new ViewAddress(DashboardView.Runtime, "uat").ToHash()));
    }
}
