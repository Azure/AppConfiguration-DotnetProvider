using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    class AzureKeyVaultClient : IAzureKeyVaultClient, IDisposable
    {
        private KeyVaultClient _keyVaultClient;

        public async Task<string> GetSecretValue(Uri secretUri, CancellationToken cancellationToken)
        {
            if (secretUri == null)
            {
                 throw new ArgumentNullException(nameof(secretUri));
            }

            EnsureInitialized();

            SecretBundle secretBundle = await _keyVaultClient.GetSecretAsync(secretUri.ToString(), cancellationToken);

            return secretBundle?.Value;
        }

        public void Dispose()
        {
            _keyVaultClient?.Dispose();
        }

        private void EnsureInitialized()
        {
            if (_keyVaultClient != null)
            {
                return;
            }

            //Use Managed identity
            var azureServiceTokenProvider = new AzureServiceTokenProvider();

            _keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
        }
    }
}
