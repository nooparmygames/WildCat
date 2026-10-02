namespace WildCat
{
    /// <summary>The player a login signed in as.</summary>
    public sealed class LoginResult
    {
        public LoginResult(string playerId, string playerName, bool isNewAccount)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            IsNewAccount = isNewAccount;
        }

        /// <summary>The player's account id, which other players use to invite or challenge them.</summary>
        public string PlayerId { get; }

        /// <summary>The player's display name; empty until Profile.SetNameAsync set one.</summary>
        public string PlayerName { get; }

        /// <summary>True when this login created the account.</summary>
        public bool IsNewAccount { get; }
    }
}
