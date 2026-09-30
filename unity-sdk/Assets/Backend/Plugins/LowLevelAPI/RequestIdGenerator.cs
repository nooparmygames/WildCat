using UnityEngine;
using System.Collections;

internal static class RequestIdGenerator
{
    private static object lockObject = new object();
    private static uint Id = uint.MaxValue;

    /// <summary>
    /// Gets the next unique Id to be used for a request
    /// </summary>
    /// <returns>A uint value from 0 to uint.MaxValue</returns>
    internal static uint GetNextId()
    {
        lock (lockObject)
        {
            if (Id == uint.MaxValue)
                Id = 0;
            else
                Id++;
        }
        return Id;
    }

}