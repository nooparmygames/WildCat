using Npgsql;
using Xunit;

namespace WildCat.Tests
{
    /// <summary>
    /// A fact that runs only when the WILDCAT_TEST_POSTGRES environment variable holds a
    /// PostgreSQL connection string, and is skipped with the reason otherwise.
    /// </summary>
    public sealed class PostgreSqlFactAttribute : FactAttribute
    {
        public const string EnvironmentVariable = "WILDCAT_TEST_POSTGRES";

        public PostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                Skip = $"Set {EnvironmentVariable} to a PostgreSQL connection string (a database with the Orleans " +
                       "scripts in deploy/postgres applied) to run the PostgreSQL provider tests.";
        }

        public static string ConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

        /// <summary>Runs a count query against the test database.</summary>
        public static async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
    }
}
