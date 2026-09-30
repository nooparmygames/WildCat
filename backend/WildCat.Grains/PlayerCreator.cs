using System;
using System.Collections.Generic;
using System.Linq;
using Orleans;
using Orleans.Runtime;
using Orleans.Concurrency;
using WildCat.Interfaces;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildCat.Grains
{
    [StatelessWorker()]
    public class PlayerCreator : Grain, IPlayerCreator
    {
        private ILogger logger;

        public PlayerCreator(ILogger<IPlayerCreator> logger)
        {
            this.logger = logger;
        }

        public Task<AccountCreationResult> CreateAccountWithDevice(string deviceId)
        {
            return CreateAccount(x => GrainFactory.GetGrain<IPlayer>(x).CreateAccountWithDevice(deviceId));
        }

        private async Task<AccountCreationResult> CreateAccount(Func<string, Task<AdditionResult>> action)
        {
            AccountCreationResult result = new AccountCreationResult();
            var userId = Guid.NewGuid().ToString();
            try
            {
                var creationResult = await action(userId);
                if (creationResult != AdditionResult.Success)
                {
                    logger.LogError(1, "Could not create the user");
                    result.result = creationResult;
                }
                else
                {
                    result.Account = userId;
                    result.result = AdditionResult.Success;
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(-1,ex.ToString());
                result.result = AdditionResult.Failure;
            }
            return result;
        }

        public Task<AccountCreationResult> CreateAccountWithEmail(string email, string password)
        {
            return CreateAccount(x => GrainFactory.GetGrain<IPlayer>(x).CreateAccountWithEmail(email, password));
        }

        public Task<AccountCreationResult> CreateAccountWithUserAndPassword(string userName, string password)
        {
            return CreateAccount(x => GrainFactory.GetGrain<IPlayer>(x).CreateAccountWithUserAndPassword(userName, password));
        }
    }
}
