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
        private readonly string _message;
        /// <summary>
        /// KeyVaultReferenceException constructor used when an Azure App Configuration reference to a Key Vault resource is invalid 
        /// </summary>
        /// <param name="message">message to tell what error is</param>
        /// <param name="inner">inner exception to show what exception it is </param>
        public KeyVaultReferenceException(string message,
                                           Exception inner)
         :base(string.Empty, inner)
        {
            _message = message;
        }

        /// <summary>
        /// Overriding Message used to show more information about the exception message 
        /// The error message that explains the reason for the exception 
        /// and attributes like the ErrorCode, key, label, etag and SeretIdentifier 
        /// </summary>
        public override string  Message => $"{_message}. ErrorCode:{ErrorCode}, Key:{Key}, Label:{Label}, Etag:{Etag}, SecretIdentifier:{SecretIdentifier}";

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
        /// The error code, if available, describing the cause of the exception. 
        /// </summary>
        public string ErrorCode { get; set; }
    }

}
