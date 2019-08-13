using System;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// The exception that is thrown when an Azure App Configuration reference to a Key Vault resource is invalid
    /// </summary>
    public class KeyVaultReferenceException : Exception
    {
        internal KeyVaultReferenceException(string message, 
                                            IKeyValue kv,
                                            Exception inner) 
            : base(message, inner)
        {
            Key = kv.Key;
            Label = kv.Label;
            Etag = kv.ETag; 
        }

        internal KeyVaultReferenceException(string message,
                                            IKeyValue kv,
                                            KeyVaultSecretReference reference,
                                            Exception inner)
            : this(message, kv, inner)
        {
            SecretIdentifier = reference.Uri.ToString();
        }

        internal KeyVaultReferenceException(string message,
                                          Exception inner)
          : base(message, inner)
        {
        }

        /// <summary>
        /// The key of the Key Vault reference that caused the exception.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The label of the Key Vault reference that caused the exception.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// The etag of the Key Vault reference that caused the exception.
        /// </summary>
        public string Etag { get; }

        /// <summary>
        /// The secret identifier used by the Azure Key Vault reference that caused the exception.
        /// </summary>
        public string SecretIdentifier { get; set; }
    }

}
