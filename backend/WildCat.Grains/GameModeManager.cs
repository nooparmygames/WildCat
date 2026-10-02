using System.Linq;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using WildCat.Interfaces;

namespace WildCat.Grains
{
    public class GameModeManager : Grain, IGameModeManager
    {
        private readonly IPersistentState<GameModeManagerState> state;

        public GameModeManager([PersistentState(GrainStateNames.GameModeManager, GrainStorageNames.Default)] IPersistentState<GameModeManagerState> state)
        {
            this.state = state;
        }

        private GameModeManagerState State => state.State;

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
            return state.WriteStateAsync();
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
                await state.WriteStateAsync();
            }
        }

    }
}
