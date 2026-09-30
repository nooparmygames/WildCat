using System;

namespace Interfaces
{
    /// <summary>
    /// This is used to get mapper names/keys using the user attribute in question, like the device id
    /// </summary>
    public class UserMappingUtility
    {
        public static string GetDeviceIdMappingId(string deviceId)
        {
            return "device_" + deviceId;
        }
    }
}
