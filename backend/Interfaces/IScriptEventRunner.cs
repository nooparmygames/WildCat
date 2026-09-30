using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IScriptEventRunner : IGrainWithGuidCompoundKey
    {
        Task<object> Run(ISession session, string eventName, params object[] args);
    }


}
