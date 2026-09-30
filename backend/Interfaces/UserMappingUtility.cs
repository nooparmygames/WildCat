using System;

namespace Interfaces
{
    /// <summary>
    /// This is used to get mapper names/keys using the user attribute in question like the device
    /// or custom provider ID or whatever
    /// </summary>
    public class UserMappingUtility
    {
        public static string GetDeviceIdMappingId(string deviceId)
        {
            return "device_" + deviceId;
        }
        
        public static string GetProviderMappingId(string providerName)
        {
            return "provider_" + providerName;
        }
    }
}
