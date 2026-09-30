namespace WildCat
{
    /// <summary>Creates a new <see cref="ITransport"/> for every connection attempt.</summary>
    public interface ITransportFactory
    {
        ITransport Create(WildCatClientOptions options);
    }
}
