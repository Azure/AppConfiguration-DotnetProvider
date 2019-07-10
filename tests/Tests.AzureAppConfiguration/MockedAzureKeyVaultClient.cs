using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class MockedAzureKeyVaultClient : IAzureKeyVaultClient
    {
        private readonly string _secretValue;

        public MockedAzureKeyVaultClient(string secretValue)
        {
            _secretValue = secretValue;
        }

        public Task<string> GetSecretValue(Uri secretUri, CancellationToken cancellationToken)
        {
            if (secretUri == null)
            {
                throw new ArgumentNullException(nameof(secretUri));
            }

            return Task.FromResult(_secretValue);
        }
    }  
}
