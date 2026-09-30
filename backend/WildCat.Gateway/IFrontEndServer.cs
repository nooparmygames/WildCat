using WildCat.Interfaces;
using Newtonsoft.Json;
using Orleans;
using Orleans.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildCat.Gateway
{
    public interface IFrontEndServer
    {
        public async Task PlayerConnected(ConnectionProcessor connection, int id){}

        public async Task PlayerDisconnected(ConnectionProcessor connection, int id) {}
        public void SetClient(IClusterClient client){}
    }
}