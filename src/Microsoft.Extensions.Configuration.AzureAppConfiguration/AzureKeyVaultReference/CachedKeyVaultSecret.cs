// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class CachedKeyVaultSecret
    {
        ///// <summary>
        ///// The value of the Key Vault secret.
        ///// </summary>
        public string SecretValue { get; set; }

        /// <summary>
        /// The time when this secret should be reloaded from Key Vault.
        /// </summary>
        public DateTimeOffset? RefreshAt { get; set; }

        /// <summary>
        /// The number of times we tried to reload this secret.
        /// </summary>
        public int RefreshAttempts { get; set; }

        public CachedKeyVaultSecret(string secretValue = null, DateTimeOffset? refreshAt = null, int refreshAttempts = 0)
        {
            SecretValue = secretValue;
            RefreshAt = refreshAt;
            RefreshAttempts = refreshAttempts;
        }
    }
}
