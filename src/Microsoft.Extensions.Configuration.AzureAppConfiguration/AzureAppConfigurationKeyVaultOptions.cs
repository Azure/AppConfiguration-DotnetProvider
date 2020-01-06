using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the client used to fetch key vault references in an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationKeyVaultOptions
    {
        internal TokenCredential Credential;
        internal List<SecretClient> SecretClients = new List<SecretClient>();

        /// <summary>
        /// Sets the credentials used to authenticate to a key vault that has no registered <see cref="SecretClient"/>.
        /// </summary>
        /// <param name="credential">Default token credentials.</param>
        public AzureAppConfigurationKeyVaultOptions SetCredential(TokenCredential credential)
        {
            Credential = credential;
            return this;
        }

        /// <summary>
        /// Registers the specified <see cref="SecretClient"/> instance to use to resolve key vault references for secrets from associated key vault.
        /// </summary>
        /// <param name="secretClient">Secret client instance.</param>
        public AzureAppConfigurationKeyVaultOptions Register(SecretClient secretClient)
        {
            SecretClients.Add(secretClient);
            return this;
        }
    }
}
