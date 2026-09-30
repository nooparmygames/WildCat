using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Interfaces;
using Orleans;

namespace Interfaces
{
    public interface IIAPVerificationManager : IGrainWithGuidKey
    {
        //getting code
        //https://pardakht.cafebazaar.ir/devapi/v2/auth/authorize/?response_type=code&access_type=offline&redirect_uri=<REDIRECT_URI>&client_id=<CLIENT_ID>
        //returns a code query string
        //getting token
        //
        //https://pardakht.cafebazaar.ir/devapi/v2/auth/token/
        //grant_type=authorization_code
        //code=<کد دریافت شده در مرحلهٔ قبل>
        //client_id=<شناسه‌ٔ مشخص شده در پنل توسعه‌دهندگان>
        //client_secret=<عبارت رمز مشخص شده در پنل توسعه‌دهندگان>
        //redirect_uri=<آدرس مشخص شده در پنل توسعه‌دهندگان
        //
        //response
        //{
        //   "access_token": "GWObRK06KHLr8pCQzDXJ9hcDdSC3eV",
        //  "token_type": "Bearer",
        //  "expires_in": 3600,
        //  "refresh_token": "yBC4br1l6OCNWnahJvreOchIZ9B6ze",
        //  "scope": "androidpublisher"
        //  }
        //
        // refresh
        //
        //https://pardakht.cafebazaar.ir/devapi/v2/auth/token/
        //grant_type = refresh_token
        //client_id=<شناسه‌ٔ مشخص شده در پنل توسعه‌دهندگان>
        //client_secret=<عبارت رمز مشخص شده در پنل توسعه‌دهندگان>
        //refresh_token=<توکن دریافتی در مرحلهٔ ۶
        //
        //{
        //"access_token": "uX5qC82EGWjkjjeyvTzTufHOM9HZfM",
        //"token_type": "Bearer",
        //"expires_in": 3600,
        //"scope": "androidpublisher"
        //}
        //
        //

        Task SaveInfo(string clientID, string secret, string token, string refreshToken, string packageName);
        Task<IAPState> GetInfo();
        Task SaveToken(string token);
    }
}

[GenerateSerializer]
[Alias("wildcat.IAPState")]
public class IAPState
{
    public const string GRANT_TYPE_AUTHORIZATION = "authorization_code";
    public const string GRANT_TYPE_REFRESH = "refresh_token";
    [Id(0)] public string ClientID { get; set; }
    [Id(1)] public string ClientSecret { get; set; }
    [Id(2)] public string RefreshToken { get; set; }
    [Id(3)] public string Token { get; set; }
    [Id(4)] public string PackageName { get; set; }

}

public class BazaarSuccessfulResponse
{
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public int expires_in { get; set; }
}