using UnityEngine;
public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    /// <summary>
    /// Check to see if we're about to be destroyed.
    /// </summary>
    private static bool shuttingDown = false;
    private static T instance;

    /// <summary>
    /// Access singleton instance through this propriety.
    /// </summary>
    public static T Instance
    {
        get
        {
            if (shuttingDown)
            {
                Debug.LogWarning($"[Singleton] Instance '{typeof(T)}' already destroyed. Returning null.");
                return null;
            }

            if (instance == null)
            {
                // Search for existing instance.
                instance = (T)FindObjectOfType(typeof(T));
            }
            return instance;
        }
    }


    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }


    private void OnDestroy()
    {
        shuttingDown = true;
    }
}