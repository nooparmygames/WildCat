using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using WildCat.Grains;
using WildCat.Gateway;
using Orleans.Configuration;
using WildCat.Host;

// One process hosts the Orleans silo and the WebSocket gateway. UseOrleans is called before the
// web host is built so the silo starts before Kestrel accepts connections, and the gateway uses the
// silo's own IClusterClient.
var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo =>
{
    silo.Configure<ClusterOptions>(o =>
    {
        o.ClusterId = "wildcat";
        o.ServiceId = "wildcat";
    });

    // Memory providers and localhost clustering in every environment until the PostgreSQL
    // providers land (roadmap step 5).
    silo.UseLocalhostClustering();
    silo.AddMemoryGrainStorageAsDefault();
    silo.AddMemoryGrainStorage("blob");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddGameStreams();
    silo.UseInMemoryReminderService();
    silo.AddLegacyCallChainReentrancy();
    silo.AddStartupTask<StartupTasks>();
});

builder.Services.Configure<ConsoleLifetimeOptions>(options => { options.SuppressStatusMessages = true; });
builder.Services.AddGameGateway(builder.Configuration.GetSection(GameGatewayOptions.SectionName));

// There is deliberately no HTTPS redirection or HSTS here: TLS terminates at the reverse proxy in
// front of the host, and clients reach it with wss://. The forwarded headers middleware takes the
// client's scheme and IP address from the proxy's X-Forwarded-Proto and X-Forwarded-For headers so
// they survive the hop. Only a proxy on loopback is trusted by default; list the address of any
// other proxy under ForwardedHeaders:KnownProxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "Running in the {Environment} environment with in-memory clustering, grain storage, streams and reminders. " +
        "All state is lost when the process stops until the PostgreSQL providers are added.",
        app.Environment.EnvironmentName);
}

app.UseForwardedHeaders();
app.UseWebSockets();
app.MapGameGateway("/ws");

app.Run();
