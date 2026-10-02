using System;
using System.Threading.Tasks;

namespace WildCat.Gateway
{
    /// <summary>
    /// The secret check of the server to server publisher data messages (62, 64), which carry the
    /// developer key and secret in their payload instead of a session.
    /// </summary>
    internal static class DeveloperSecret
    {
        /// <summary>Error text (code 1) for an unknown developer key or a wrong secret.</summary>
        public const string Wrong = "The developer key or secret is wrong.";

        /// <summary>
        /// Runs a DeveloperStorage call that takes the secret. Its InvalidOperationException, which
        /// it throws only for a secret that is not the developer's current one (or a developer that
        /// does not exist), becomes error code 1.
        /// </summary>
        public static async Task<T> CheckedAsync<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (InvalidOperationException)
            {
                throw new RequestFailedException(GatewayErrors.Refused, Wrong);
            }
        }

        /// <inheritdoc cref="CheckedAsync{T}(Func{Task{T}})"/>
        public static Task CheckedAsync(Func<Task> call)
        {
            return CheckedAsync(async () =>
            {
                await call();
                return true;
            });
        }
    }
}
