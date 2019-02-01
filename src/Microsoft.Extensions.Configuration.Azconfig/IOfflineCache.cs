namespace Microsoft.Extensions.Configuration.Azconfig
{
    public interface IOfflineCache
    {
        string Import();
        void Export(string data);
    }
}
