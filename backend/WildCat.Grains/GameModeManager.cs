using System.Linq;
using System.Threading.Tasks;
using Orleans;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class GameModeManager : Grain<GameModeManagerState>, IGameModeManager
    {
        public Task AddOrUpdateGameMode(GameMode newMode)
        {
            var existingIndex = State.GameModes.FindIndex(x => x.Name == newMode.Name);
            if (existingIndex < 0)
            {
                State.GameModes.Add(newMode);
            }
            else
            {
                State.GameModes[existingIndex] = newMode;
            }
            return WriteStateAsync();
        }

        public Task<GameMode> GetGameModeData(string name)
        {
            var foundItem = State.GameModes.FirstOrDefault(x => x.Name == name);
            return Task.FromResult(foundItem);
        }

        public Task<GameModeManagerState> GetAllGameModes()
        {
            return Task.FromResult(State);
        }

        public async Task Remove(string name)
        {
            var index = State.GameModes.FindIndex(x => x.Name == name);
            if (index >= 0)
            {
                State.GameModes.RemoveAt(index);
                await WriteStateAsync();
            }
        }

    }
}
