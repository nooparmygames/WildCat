using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IPlayerAchievementManager : IGrainWithStringKey
    {
        Task<string[]> GetAchievements();
        Task PlayerAchieved(IPlayer player, string achievement);
    }
}
