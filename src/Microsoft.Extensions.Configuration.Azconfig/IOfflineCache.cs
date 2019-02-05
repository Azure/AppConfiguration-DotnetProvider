namespace Microsoft.Extensions.Configuration.Azconfig
{
    public interface IOfflineCache
    {
        string Import(AzconfigOptions options);
        void Export(AzconfigOptions options, string data);
    }
}
