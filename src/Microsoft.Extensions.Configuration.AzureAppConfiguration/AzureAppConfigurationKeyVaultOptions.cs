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
        internal TokenCredential Credential;
        internal List<SecretClient> SecretClients = new List<SecretClient>();
        internal Func<Uri, ValueTask<string>> SecretResolver;

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
        /// Sets the callback to be invoked for resolving those key vault references that have no registered <see cref="SecretClient"/>.
        /// </summary>
        /// <param name="secretResolver">A callback which returns a value for the given Key Vault reference.</param>
        public AzureAppConfigurationKeyVaultOptions SetSecretResolver(Func<Uri, ValueTask<string>> secretResolver)
        {
            SecretResolver = secretResolver;
            return this;
        }
    }
}
