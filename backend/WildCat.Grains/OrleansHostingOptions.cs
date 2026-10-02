namespace WildCat.Grains
{
    /// <summary>
    /// Which backing store the silo uses for clustering, grain state and reminders.
    /// </summary>
    public enum OrleansProvider
    {
        /// <summary>
        /// Localhost clustering, memory grain storage and in-memory reminders. Everything is lost
        /// when the process stops. Meant for development and tests.
        /// </summary>
        Memory,

        /// <summary>
        /// ADO.NET clustering, grain storage and reminders on PostgreSQL through Npgsql. The
        /// database needs the Orleans PostgreSQL scripts (main, clustering, persistence,
        /// reminders) applied before the first start.
        /// </summary>
        PostgreSQL,
    }

    /// <summary>
    /// How the host runs its Orleans silo, bound from the "WildCat:Orleans" configuration section
    /// (environment variables use a double underscore, for example WildCat__Orleans__Provider).
    /// The connection string is not part of these options; it is read from
    /// ConnectionStrings:WildCat. See <see cref="SiloBuilderExtensions.AddWildCatProviders"/>.
    /// </summary>
    public class OrleansHostingOptions
    {
        /// <summary>The configuration section these options are read from.</summary>
        public const string SectionName = "WildCat:Orleans";

        /// <summary>The name of the connection string (ConnectionStrings:WildCat) used by the PostgreSQL provider.</summary>
        public const string ConnectionStringName = "WildCat";

        /// <summary>The ADO.NET invariant for PostgreSQL. Orleans loads the Npgsql factory by this name.</summary>
        public const string PostgreSqlInvariant = "Npgsql";

        /// <summary>Memory (the default) or PostgreSQL.</summary>
        public OrleansProvider Provider { get; set; } = OrleansProvider.Memory;

        /// <summary>
        /// Which silos form one cluster. Silos only join others with the same ClusterId. It can
        /// change between deployments (for example a new cluster for an upgrade).
        /// </summary>
        public string ClusterId { get; set; } = "wildcat-dev";

        /// <summary>
        /// Identifies the service across deployments. Grain state and reminders are keyed by it,
        /// so it must stay the same for as long as the stored data should be found again.
        /// </summary>
        public string ServiceId { get; set; } = "wildcat";

        /// <summary>Port for silo-to-silo traffic.</summary>
        public int SiloPort { get; set; } = 11111;

        /// <summary>Port for Orleans clients connecting to this silo (not the WebSocket gateway).</summary>
        public int GatewayPort { get; set; } = 30000;

        /// <summary>
        /// Host name or IP address other silos use to reach this one. Only used by the PostgreSQL
        /// provider. When empty, the silo advertises the first non-loopback IPv4 address of the
        /// machine. With the PostgreSQL provider the silo listens on every address either way,
        /// which is what a container needs. The Memory provider always uses loopback.
        /// </summary>
        public string AdvertisedHost { get; set; } = "";
    }
}
