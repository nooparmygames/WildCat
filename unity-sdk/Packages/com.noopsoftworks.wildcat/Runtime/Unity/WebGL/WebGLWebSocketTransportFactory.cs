namespace WildCat.Unity
{
    /// <summary>Creates <see cref="WebGLWebSocketTransport"/>s; WildCatBehaviour uses it in WebGL players.</summary>
    public sealed class WebGLWebSocketTransportFactory : ITransportFactory
    {
        public ITransport Create(WildCatClientOptions options)
        {
            return new WebGLWebSocketTransport(options.MaxMessageBytes);
        }
    }
}
