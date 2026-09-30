using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Orleans;

namespace Interfaces
{
    public interface IIAPVerification : IGrainWithGuidCompoundKey
    {
        //https://pardakht.cafebazaar.ir/devapi/v2/api/validate/<package_name>/inapp/<product_id>/purchases/<purchase_token>/
        //query string access_token=<token>
        Task<Tuple<VerificationStatus,string>> VerifyCafeBazaar(string product, string Receipt,bool isFirstAtempt=true);
    }

    [GenerateSerializer, Alias("wildcat.VerificationStatus")]
    public enum VerificationStatus : byte { Verified, WasFraud, TokenExpired, FailedToConnect }
}
