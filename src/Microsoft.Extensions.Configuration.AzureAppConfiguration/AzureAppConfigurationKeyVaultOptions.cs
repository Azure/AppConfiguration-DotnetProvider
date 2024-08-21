// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the client used to fetch key vault references in an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationKeyVaultOptions
    {
        internal TokenCredential? Credential;
        internal SecretClientOptions ClientOptions = new SecretClientOptions();
        internal List<SecretClient> SecretClients = new List<SecretClient>();
        internal Func<Uri, ValueTask<string>>? SecretResolver;
        internal Dictionary<string, TimeSpan> SecretRefreshIntervals = new Dictionary<string, TimeSpan>();
        internal TimeSpan? DefaultSecretRefreshInterval = null;
        internal bool IsKeyVaultRefreshConfigured = false;

        /// <summary>
        /// Sets the credentials used to authenticate to key vaults that have no registered <see cref="SecretClient"/>.
        /// </summary>
        /// <param name="credential">Default token credentials.</param>
        [MemberNotNull(nameof(Credential))]
        public AzureAppConfigurationKeyVaultOptions SetCredential(TokenCredential credential)
        {
            Credential = credential;
            return this;
        }

        /// <summary>
        /// Configures the client options used when connecting to key vaults that have no registered <see cref="SecretClient"/>. 
        /// The client options will not affect <see cref="SecretClient"/> instances registered via <see cref="Register(SecretClient)"/>.
        /// </summary>
        /// <param name="configure">A callback used to configure secret client options.</param>
        public AzureAppConfigurationKeyVaultOptions ConfigureClientOptions(Action<SecretClientOptions> configure)
        {
            configure?.Invoke(ClientOptions);
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
        [MemberNotNull(nameof(SecretResolver))]
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
        /// Sets the refresh interval for periodically reloading a secret from Key Vault. 
        /// Any refresh operation triggered using <see cref="IConfigurationRefresher"/> will not update the value for a Key Vault secret until the cached value for that secret has expired.
        /// </summary>
        /// <param name="secretReferenceKey">Key of the Key Vault reference in Azure App Configuration.</param>
        /// <param name="refreshInterval">Minimum time that must elapse before the secret is reloaded from Key Vault.</param>
        public AzureAppConfigurationKeyVaultOptions SetSecretRefreshInterval(string secretReferenceKey, TimeSpan refreshInterval)
        {
            if (string.IsNullOrEmpty(secretReferenceKey))
            {
                throw new ArgumentNullException(nameof(secretReferenceKey));
            }

            if (refreshInterval < RefreshConstants.MinimumSecretRefreshInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), refreshInterval.TotalMilliseconds,
                    string.Format(ErrorMessages.SecretRefreshIntervalTooShort, RefreshConstants.MinimumSecretRefreshInterval.TotalMilliseconds));
            }

            SecretRefreshIntervals[secretReferenceKey] = refreshInterval;
            IsKeyVaultRefreshConfigured = true;
            return this;
        }

        /// <summary>
        /// Sets the refresh interval for periodically reloading all those secrets which do not have individual refresh intervals. 
        /// Any refresh operation triggered using <see cref="IConfigurationRefresher"/> will not update the value for a Key Vault secret until the cached value for that secret has expired.
        /// </summary>
        /// <param name="refreshInterval">Minimum time that must elapse before the secrets are reloaded from Key Vault.</param>
        public AzureAppConfigurationKeyVaultOptions SetSecretRefreshInterval(TimeSpan refreshInterval)
        {
            if (refreshInterval < RefreshConstants.MinimumSecretRefreshInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), refreshInterval.TotalMilliseconds,
                    string.Format(ErrorMessages.SecretRefreshIntervalTooShort, RefreshConstants.MinimumSecretRefreshInterval.TotalMilliseconds));
            }

            DefaultSecretRefreshInterval = refreshInterval;
            IsKeyVaultRefreshConfigured = true;
            return this;
        }
    }
}
