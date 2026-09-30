using System;
using System.Collections.Generic;
using Orleans;
using System.Threading.Tasks;

namespace Interfaces
{
    /// <summary>
    /// A stateless grain for creating accounts
    /// </summary>
    public interface IPlayerCreator :IGrainWithIntegerKey
    {
        Task<AccountCreationResult> CreateAccountWithDevice(string deviceId);
        Task<AccountCreationResult> CreateAccountWithUserAndPassword(string userName,string password);
        Task<AccountCreationResult> CreateAccountWithEmail(string email, string password);
        Task<AccountCreationResult> CreateAccountWithProvider(string providerName, Dictionary<string,string> data);
    }

    public class AccountCreationResult
    {
        public string Account { get; set; }
        public AdditionResult result;
    }
}
