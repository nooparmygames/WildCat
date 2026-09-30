using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Interfaces;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace Collections
{
    public class IAPVerification : Grain, IIAPVerification
    {
        private IAPState state;
        private Guid gameKey;

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            string player;
            gameKey = this.GetPrimaryKey(out player);
            state = await GrainFactory.GetGrain<IIAPVerificationManager>(gameKey).GetInfo();
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<Tuple<VerificationStatus, string>> VerifyCafeBazaar(string product, string Receipt, bool isFirstAtempt = true)
        {
            var c = HttpClientUtility.Get();
            try
            {
                var response = await c.GetAsync($"https://pardakht.cafebazaar.ir/devapi/v2/api/validate/{state.PackageName}/inapp/{product}/purchases/{Receipt}/?access_token={state.Token}");
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine(response.StatusCode);
                Console.WriteLine(content);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    c.Dispose();
                    return Tuple.Create(VerificationStatus.Verified, content);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    c.Dispose();
                    return Tuple.Create(VerificationStatus.WasFraud, content);
                }
                else
                {
                    var token = await RefreshToken(c);
                    c.Dispose();
                    if (string.IsNullOrEmpty(token) || !isFirstAtempt)
                    {
                        return Tuple.Create(VerificationStatus.FailedToConnect, content);
                    }
                    string player;
                    gameKey = this.GetPrimaryKey(out player);
                    await GrainFactory.GetGrain<IIAPVerificationManager>(gameKey).SaveToken(token);
                    state.Token = token;
                    return await VerifyCafeBazaar(product, Receipt, false);
                }
            }
            catch (Exception x)
            {
                c.Dispose();
                return Tuple.Create(VerificationStatus.FailedToConnect, x.Message);
            }
        }

        private async Task<string> RefreshToken(HttpClient c)
        {
            FormUrlEncodedContent msg = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type", "refresh_token"),
                new KeyValuePair<string,string>("client_id", state.ClientID),
                new KeyValuePair<string,string>("client_secret", state.ClientSecret),
                new KeyValuePair<string,string>("refresh_token", state.RefreshToken)
            });
            var s = await c.PostAsync("https://pardakht.cafebazaar.ir/devapi/v2/auth/token/", msg);
            Console.WriteLine(s.StatusCode);
            if (s.IsSuccessStatusCode)
            {
                var responseString = await s.Content.ReadAsStringAsync();

                var jsonData = JObject.Parse(responseString);
                Console.WriteLine(jsonData);
                return jsonData["access_token"].ToString();
            }
            else
            {
                return "";
            }
        }

    }
}
