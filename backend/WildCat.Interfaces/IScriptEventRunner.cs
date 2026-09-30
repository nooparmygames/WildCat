using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    /// <summary>
    /// Runs script events for one player of one game. The key is the game key plus the player's
    /// account id as the key extension; the gateway uses the caller's session for both, so a
    /// script always runs on behalf of the player who sent the request.
    /// </summary>
    public interface IScriptEventRunner : IGrainWithGuidCompoundKey
    {
        /// <summary>
        /// Runs <paramref name="eventName"/> with <paramref name="args"/>, which come straight from
        /// the game client (ExecuteScriptEvent) and must be treated as untrusted input by the
        /// script. The session must belong to the player and game in this grain's key. Throws
        /// ScriptTimeoutException when the script runs past the configured limit.
        /// </summary>
        Task<object> Run(ISession session, string eventName, params object[] args);
    }
}
