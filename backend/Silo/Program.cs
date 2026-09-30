using Collections;
using FrontEnd;
using Interfaces;
using Orleans.Configuration;
using SiloHost;

// One process hosts the Orleans silo and the WebSocket gateway. UseOrleans is called before the
// web host is built so the silo starts before Kestrel accepts connections, and the gateway uses the
// silo's own IClusterClient.
var builder = WebApplication.CreateBuilder(args);

builder.UseOrleans(silo =>
{
    silo.Configure<ClusterOptions>(o =>
    {
        o.ClusterId = "apadana";
        o.ServiceId = "apadana";
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

builder.Services.AddSingleton<IBackendStorageServer, PlayfabManager>();
builder.Services.Configure<ConsoleLifetimeOptions>(options => { options.SuppressStatusMessages = true; });
builder.Services.AddGameGateway();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "Running in the {Environment} environment with in-memory clustering, grain storage, streams and reminders. " +
        "All state is lost when the process stops until the PostgreSQL providers are added.",
        app.Environment.EnvironmentName);
}

app.UseWebSockets();
app.MapGameGateway("/ws");

app.Run();
