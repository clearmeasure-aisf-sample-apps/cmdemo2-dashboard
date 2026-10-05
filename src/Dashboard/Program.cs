using Dashboard;
using Dashboard.Health;
using Dashboard.Runtime;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One HttpClient for topology.json and runtime/ (relative to the dashboard's own address), for the nodes and for the
// pinned versions (absolute addresses).
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<TopologyLoader>();
builder.Services.AddScoped<NodeProber>();
builder.Services.AddScoped<PinnedVersionsReader>();
builder.Services.AddScoped<RuntimeLoader>();

await builder.Build().RunAsync();
