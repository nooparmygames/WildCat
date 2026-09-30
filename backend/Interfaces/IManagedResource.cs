using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;

namespace Interfaces
{
    /// <summary>
    /// Any resource which should have a representation in DB to be valid like leaderboard types, match-maker types and ...
    /// should implement this and also have a manager associated with it to manage valid possibilities.
    /// </summary>
    public interface IManagedResource : IGrainWithStringKey
    {
        Task<string> GetKey();
        Task<string> GetName();
    }
}
