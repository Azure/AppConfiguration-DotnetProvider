namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IAppConfigClient
    {
        Task<IEnumerable<IKeyValue>> GetSettings(string appConfigUri, string prefix);

        Task<IKeyValue> GetSetting(string appConfigUri, string key);

        Task<string> GetETag(string appConfigUri, string key);
    }
}