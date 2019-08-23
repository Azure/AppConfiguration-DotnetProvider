using Azure.ApplicationModel.Configuration;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        IEnumerable<KeyValuePair<string, string>> GetKeyValues(ConfigurationSetting keyValue);
    }
}
