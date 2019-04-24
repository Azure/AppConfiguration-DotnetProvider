using System.Collections.Generic;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        IEnumerable<KeyValuePair<string, string>> GetKeyValues(IKeyValue keyValue);
    }
}
