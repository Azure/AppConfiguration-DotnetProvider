using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(IKeyValue keyValue, CancellationToken cancellationToken);

        bool CanProcess(IKeyValue kv);
    }
}
