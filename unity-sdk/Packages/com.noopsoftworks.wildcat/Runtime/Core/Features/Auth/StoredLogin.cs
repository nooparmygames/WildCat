using WildCat.Protocol;

namespace WildCat
{
    /// <summary>The last successful login, kept in memory to log in again after a reconnection.</summary>
    internal sealed class StoredLogin
    {
        public StoredLogin(LoginType type, string id, string password, bool createAccount)
        {
            Type = type;
            Id = id;
            Password = password ?? "";
            CreateAccount = createAccount;
        }

        public LoginType Type { get; }

        public string Id { get; }

        public string Password { get; }

        public bool CreateAccount { get; }
    }
}
