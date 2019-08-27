using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        Task<IEnumerable<KeyValuePair<string, string>>> GetKeyValues(ConfigurationSetting setting);

        bool CanProcess(ConfigurationSetting setting);
    }
}
