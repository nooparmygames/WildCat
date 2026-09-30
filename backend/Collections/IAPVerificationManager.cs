using System;
using System.Collections.Generic;
using System.Linq;
using Orleans;
using System.Threading.Tasks;
using Interfaces;

namespace Collections
{
    public class IAPVerificationManager : Grain<IAPState>, IIAPVerificationManager
    {
        public Task<IAPState> GetInfo()
        {
            if (!string.IsNullOrEmpty(State.Token))
                return Task.FromResult(State);
            throw new InvalidOperationException();
        }

        public Task SaveToken(string token)
        {
            State.Token = token;
            return WriteStateAsync();
        }

        public Task SaveInfo(string clientID, string secret, string token, string refreshToken, string packageName)
        {
            State.ClientID = clientID;
            State.ClientSecret = secret;
            State.Token = token;
            State.RefreshToken = refreshToken;
            State.PackageName = packageName;
            return WriteStateAsync();
        }
    }
}
