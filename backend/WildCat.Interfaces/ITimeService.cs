using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace WildCat.Interfaces
{
    public interface ITimeService : IGrainWithGuidKey
    {
        Task<string> GetServerTime();
    }
}
