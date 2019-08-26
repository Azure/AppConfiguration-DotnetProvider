using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        Task<IEnumerable<KeyValuePair<string, string>>> GetKeyValues(IKeyValue keyValue);

        bool CanProcess(IKeyValue kv);
    }
}
