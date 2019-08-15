using System;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// The exception that is thrown when an Azure App Configuration reference to a Key Vault resource is invalid
    /// </summary>
    public class KeyVaultReferenceException : Exception
    {
     
        internal KeyVaultReferenceException(string message,
                                           Exception inner = null)
         : base(message, inner)
        {
        }

        internal KeyVaultReferenceException(string message, 
                                            IKeyValue kv,
                                            Exception inner) 
            : base(message, inner)
        {
            Key = kv.Key;
            Label = kv.Label;
            Etag = kv.ETag;
            ErrorCode = (inner as KeyVaultErrorException)?.Body?.Error?.InnerError?.Code;
        }

        internal KeyVaultReferenceException(string message,
                                            IKeyValue kv,
                                            KeyVaultSecretReference reference,
                                            Exception inner)
            : this(message, kv, inner)
        {
            SecretIdentifier = reference.Uri;
        }

      

        /// <summary>
        /// The key of the Key Vault reference that caused the exception.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The label of the Key Vault reference that caused the exception.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The etag of the Key Vault reference that caused the exception.
        /// </summary>
        public string Etag { get; set; }

        /// <summary>
        /// The secret identifier used by the Azure Key Vault reference that caused the exception.
        /// </summary>
        public string SecretIdentifier { get; set; }

        /// <summary>
        /// The Inner Error code message that is returned in the exception 
        /// </summary>
        public string ErrorCode { get; set; }
    }

}
