// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal struct CachedKeyVaultSecret
    {
        ///// <summary>
        ///// The value of the Key Vault secret.
        ///// </summary>
        public string SecretValue { get; set; }

        /// <summary>
        /// The time when this secret should be reloaded from Key Vault.
        /// </summary>
        public DateTimeOffset? RefreshAt { get; set; }

        public CachedKeyVaultSecret(string secretValue, DateTimeOffset? refreshAt)
        {
            SecretValue = secretValue;
            RefreshAt = refreshAt;
        }
    }
}
