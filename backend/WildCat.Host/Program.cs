using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using WildCat.Grains;
using WildCat.Gateway;

// One process hosts the Orleans silo and the WebSocket gateway. UseOrleans is called before the
// web host is built so the silo starts before Kestrel accepts connections, and the gateway uses the
// silo's own IClusterClient.
var builder = WebApplication.CreateBuilder(args);

// WildCat:Orleans picks the providers (Memory or PostgreSQL), ids and ports; the PostgreSQL
// provider reads ConnectionStrings:WildCat. Both are described in appsettings.json.
var orleansOptions = builder.Configuration.GetSection(OrleansHostingOptions.SectionName).Get<OrleansHostingOptions>()
                     ?? new OrleansHostingOptions();
var connectionString = builder.Configuration.GetConnectionString(OrleansHostingOptions.ConnectionStringName);

builder.UseOrleans(silo =>
{
    silo.AddWildCatProviders(orleansOptions, connectionString);
    silo.AddGameStreams();
    silo.AddLegacyCallChainReentrancy();
});

// Account emails go through SMTP when WildCat:Email has a host and sender, otherwise to the log.
builder.Services.AddWildCatEmail(builder.Configuration.GetSection(EmailOptions.SectionName));

// Registered after UseOrleans so the silo is running when the seed starts.
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
builder.Services.AddHostedService<SeedHostedService>();

builder.Services.Configure<ConsoleLifetimeOptions>(options => { options.SuppressStatusMessages = true; });
builder.Services.AddGameGateway(builder.Configuration.GetSection(GameGatewayOptions.SectionName));

// There is deliberately no HTTPS redirection or HSTS here: TLS terminates at the reverse proxy in
// front of the host, and clients reach it with wss://. The forwarded headers middleware takes the
// client's scheme and IP address from the proxy's X-Forwarded-Proto and X-Forwarded-For headers so
// they survive the hop. Only a proxy on loopback is trusted by default; list the address of any
// other proxy under ForwardedHeaders:KnownProxies (single addresses, such as "10.0.0.5") or
// ForwardedHeaders:KnownNetworks (CIDR ranges, such as "172.16.0.0/12" for a Docker network).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

if (orleansOptions.Provider == OrleansProvider.Memory && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "Running in the {Environment} environment with the Memory provider: clustering, grain state and reminders " +
        "live in this process and are lost when it stops. Set {Section}:Provider to PostgreSQL to keep them.",
        app.Environment.EnvironmentName, OrleansHostingOptions.SectionName);
}

app.UseForwardedHeaders();
app.UseWebSockets();
app.MapGameGateway("/ws");

app.Run();
