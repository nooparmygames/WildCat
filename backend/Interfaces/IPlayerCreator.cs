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
    }

    [GenerateSerializer]
    [Alias("wildcat.AccountCreationResult")]
    public class AccountCreationResult
    {
        [Id(0)] public string Account { get; set; }
        [Id(1)] public AdditionResult result;
    }
}
