// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the client used to fetch key vault references in an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationKeyVaultOptions
    {
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromHours(12);
        private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromHours(1);

        internal TokenCredential Credential;
        internal List<SecretClient> SecretClients = new List<SecretClient>();
        internal Func<Uri, ValueTask<string>> SecretResolver;
        internal Dictionary<string, TimeSpan> SecretRefreshIntervals = new Dictionary<string, TimeSpan>();

        /// <summary>
        /// If true, certificates will be reloaded from Key Vault based on their auto-renewal policy.
        /// </summary>
        public bool? UseCertificateRotationPolicy { get; set; } = null;

        /// <summary>
        /// Sets the credentials used to authenticate to key vaults that have no registered <see cref="SecretClient"/>.
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

        /// <summary>
        /// Sets the callback used to resolve key vault references that have no registered <see cref="SecretClient"/>.
        /// </summary>
        /// <param name="secretResolver">A callback that maps the <see cref="Uri"/> of the key vault secret to its value.</param>
        public AzureAppConfigurationKeyVaultOptions SetSecretResolver(Func<Uri, ValueTask<string>> secretResolver)
        {
            if (secretResolver == null)
            {
                throw new ArgumentNullException(nameof(secretResolver));
            }

            SecretResolver = secretResolver;
            return this;
        }

        /// <summary>
        /// Sets the refresh interval for periodically reloading a secret from Key Vault. Refresh interval must be greater than 1 hour. Default refresh interval is 12 hours.
        /// Any refresh operation triggered using <see cref="IConfigurationRefresher"/> will not update the value for a Key Vault secret until the cached value for that secret has expired.
        /// </summary>
        /// <param name="key">Key of the Key Vault reference in Azure App Configuration.</param>
        /// <param name="refreshInterval">Minimum time that must elapse before the secret is reloaded from Key Vault.</param>
        public AzureAppConfigurationKeyVaultOptions SetSecretRefreshInterval(string key, TimeSpan? refreshInterval = null)
        {
            if (refreshInterval != null && refreshInterval < MinimumRefreshInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), refreshInterval?.TotalHours,
                    string.Format(ErrorMessages.SecretRefreshIntervalTooShort, MinimumRefreshInterval.TotalHours));
            }

            SecretRefreshIntervals[key] = refreshInterval ?? DefaultRefreshInterval;

            return this;
        }
    }
}
