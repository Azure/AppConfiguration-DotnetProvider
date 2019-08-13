using System;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// The exception that is thrown when an Azure App Configuration reference to a Key Vault resource is invalid
    /// </summary>
    public class KeyVaultReferenceException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyVaultReferenceException"/> class. 
        /// </summary>
        /// <param name="message">Gets a message that describes the current exception</param>
        /// <param name="inner">Gets the <see cref="System.Exception" /> instance that caused the current exception.</param>
        public KeyVaultReferenceException(string message, Exception inner) : base (message, inner)
        {
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
    }

}
