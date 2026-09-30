using System;
using UnityEngine;

namespace WildCat.Unity
{
    /// <summary>Writes the SDK's log messages to the Unity console. Safe from any thread.</summary>
    public sealed class UnityWildCatLogger : IWildCatLogger
    {
        private readonly WildCatLogLevel minimum;

        public UnityWildCatLogger(WildCatLogLevel minimum)
        {
            this.minimum = minimum;
        }

        public void Log(WildCatLogLevel level, string message, Exception exception)
        {
            if (level < minimum || level == WildCatLogLevel.None)
                return;
            string text = "[WildCat] " + message;
            switch (level)
            {
                case WildCatLogLevel.Error:
                    Debug.LogError(text);
                    break;
                case WildCatLogLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
            if (exception != null)
                Debug.LogException(exception);
        }
    }
}
