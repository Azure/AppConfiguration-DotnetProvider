// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class CachedKeyVaultSecret
    {
        /// <summary>
        /// Key of the Key Vault reference in App Configuration.
        /// </summary>
        public string Key { get; set; }

        ///// <summary>
        ///// The value of the Key Vault secret.
        ///// </summary>
        public string SecretValue { get; set; }

        /// <summary>
        /// The cache expiration time for the Key Vault secret.
        /// </summary>
        public DateTimeOffset? ExpiresOn { get; set; }

        public CachedKeyVaultSecret(string key)
        {
            Key = key;
        }

        public override bool Equals(object obj)
        {
            if (obj is CachedKeyVaultSecret cachedSecret)
            {
                return Key == cachedSecret.Key;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }
    }
}
