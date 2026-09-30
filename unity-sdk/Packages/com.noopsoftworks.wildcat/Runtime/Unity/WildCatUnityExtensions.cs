using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WildCat.Unity
{
    /// <summary>Unity conveniences on top of the engine-free client.</summary>
    public static class WildCatUnityExtensions
    {
        private const string FallbackDeviceIdKey = "WildCat.DeviceId";

        /// <summary>For coroutines: <c>yield return client.Profile.GetNameAsync().AsCoroutine();</c></summary>
        public static TaskYieldInstruction AsCoroutine(this Task task)
        {
            return new TaskYieldInstruction(task);
        }

        /// <summary>For coroutines, with the result in <see cref="TaskYieldInstruction{T}.Result"/>.</summary>
        public static TaskYieldInstruction<T> AsCoroutine<T>(this Task<T> task)
        {
            return new TaskYieldInstruction<T>(task);
        }

        /// <summary>
        /// This device's id: SystemInfo.deviceUniqueIdentifier, or, where the platform has none
        /// (WebGL), a random id kept in PlayerPrefs. Call it on the main thread.
        /// </summary>
        public static string ThisDeviceId
        {
            get
            {
                string id = SystemInfo.deviceUniqueIdentifier;
                if (!string.IsNullOrEmpty(id) && id != SystemInfo.unsupportedIdentifier)
                    return id;
                id = PlayerPrefs.GetString(FallbackDeviceIdKey, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(FallbackDeviceIdKey, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        /// <summary>Logs in with <see cref="ThisDeviceId"/>, creating the account on first use. Call it on the main thread.</summary>
        public static Task<LoginResult> LoginWithThisDeviceAsync(this AuthClient auth, bool createAccount = true,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return auth.LoginWithDeviceAsync(ThisDeviceId, createAccount, cancellationToken);
        }

        /// <summary>Creates an account for <see cref="ThisDeviceId"/> without logging in. Call it on the main thread.</summary>
        public static Task<string> CreateAccountWithThisDeviceAsync(this AuthClient auth,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return auth.CreateAccountWithDeviceAsync(ThisDeviceId, cancellationToken);
        }
    }
}
