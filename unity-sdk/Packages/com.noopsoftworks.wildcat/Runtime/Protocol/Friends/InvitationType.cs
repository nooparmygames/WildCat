namespace WildCat.Protocol
{
    /// <summary>What the value of an InviteFriend request (90) identifies the other player by.</summary>
    public enum InvitationType : byte { AccountId, playerName, Email, UserId }
}
