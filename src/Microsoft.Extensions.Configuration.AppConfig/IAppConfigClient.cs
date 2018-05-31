namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IAppConfigClient
    {
        Task<IEnumerable<IKeyValue>> GetSettings(string prefix);

        Task<IKeyValue> GetSetting(string key);

        Task<string> GetETag(string key);
    }
}