namespace Microsoft.Extensions.Configuration.Azconfig
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IAzconfigClient
    {
        Task<IEnumerable<IKeyValue>> GetSettings(string prefix);

        Task<IKeyValue> GetSetting(string key);

        Task<string> GetETag(string key);
    }
}