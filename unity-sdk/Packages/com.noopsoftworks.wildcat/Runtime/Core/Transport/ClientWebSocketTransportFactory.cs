using System;
using System.Net.WebSockets;

namespace WildCat
{
    /// <summary>Creates <see cref="ClientWebSocketTransport"/>s. The default transport factory.</summary>
    public sealed class ClientWebSocketTransportFactory : ITransportFactory
    {
        private readonly Action<ClientWebSocketOptions> configure;

        /// <param name="configure">Changes to each socket's options before it connects, or null.</param>
        public ClientWebSocketTransportFactory(Action<ClientWebSocketOptions> configure = null)
        {
            this.configure = configure;
        }

        public ITransport Create(WildCatClientOptions options)
        {
            return new ClientWebSocketTransport(options.MaxMessageBytes, configure);
        }
    }
}
