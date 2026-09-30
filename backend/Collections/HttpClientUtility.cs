using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace Collections
{
    public static class HttpClientUtility
    {
        //private static AsyncLocal<HttpClient> client = new AsyncLocal<HttpClient>();

        public static HttpClient Get()
        {
            return new HttpClient();// client.Value;
        }
    }
}
