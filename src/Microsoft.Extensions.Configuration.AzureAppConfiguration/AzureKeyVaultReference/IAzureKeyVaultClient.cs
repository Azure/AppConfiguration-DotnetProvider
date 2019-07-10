using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal interface IAzureKeyVaultClient
    {
        Task<string> GetSecretValue(Uri secretUri, CancellationToken cancellationToken);
    }
}
