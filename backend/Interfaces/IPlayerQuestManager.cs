using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPlayerQuestManager:IGrainWithStringKey
    {
        Task<string> GetQuests();
        Task SetProgress(IPlayer player, string quest, byte progress);
    }
}
