namespace WildCat.Grains
{
    /// <summary>
    /// Developers and games created at startup when they do not exist yet, bound from the
    /// "WildCat:Seed" configuration section. appsettings.Development.json seeds one developer and
    /// game with placeholder keys; appsettings.json seeds nothing. See <see cref="SeedHostedService"/>.
    /// </summary>
    public class SeedOptions
    {
        /// <summary>The configuration section these options are read from.</summary>
        public const string SectionName = "WildCat:Seed";

        public List<SeedDeveloper> Developers { get; set; } = new List<SeedDeveloper>();
    }

    public class SeedDeveloper
    {
        /// <summary>The developer key clients send when they initialize a session.</summary>
        public Guid Key { get; set; }

        /// <summary>The developer's first name.</summary>
        public string Name { get; set; } = "";

        /// <summary>The developer's email, which identifies the developer in the developer manager.</summary>
        public string Email { get; set; } = "";

        public List<SeedGame> Games { get; set; } = new List<SeedGame>();
    }

    public class SeedGame
    {
        /// <summary>The game key clients send when they initialize a session.</summary>
        public Guid Key { get; set; }

        /// <summary>The game name, unique per developer.</summary>
        public string Name { get; set; } = "";
    }
}
