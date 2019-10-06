using Azure.Data.AppConfiguration;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class MockedAzureKeyVaultClient : MockedAzureKeyVaultClientBase
    {
        private readonly string _secretValue;

        public bool IsEnabled { get; set; } = true;

        public bool HasAccessToKeyVault { get; set; } = true;

        public CancellationToken CancellationToken { get; set; }

        public ConfigurationSetting kv { get; set; }

        public KeyVaultSecretReference secretRef { get; }

        public MockedAzureKeyVaultClient(string secretValue)
        {
            _secretValue = secretValue;
        }

        public override Task<AzureOperationResponse<SecretBundle>> GetSecretWithHttpMessagesAsync(
            string vaultBaseUrl,
            string secretName,
            string secretVersion,
            Dictionary<string, List<string>> customHeaders = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                throw new KeyVaultErrorException()
                {
                    Body = new KeyVaultError(
                        new Error("Forbidden", "Operation get is not allowed on a disabled secret.",
                        new Error("SecretDisabled", null, null)))
                };
            }

            if (!HasAccessToKeyVault)
            {
                throw new KeyVaultErrorException()
                {
                    Body = new KeyVaultError(
                        new Error("Forbidden", "Access denied. Caller was not found on any access policy",
                        new Error("AccessDenied", null, null)))
                };
            }

            CancellationToken.ThrowIfCancellationRequested();

            var response = new AzureOperationResponse<SecretBundle>()
            {
                RequestId = Guid.NewGuid().ToString(),
                Body = new SecretBundle(_secretValue)
            };

            return Task.FromResult(response);
        }
    }
}
