using System;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// 
    /// </summary>
    public class KeyVaultReferenceException : Exception
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="inner"></param>
        public KeyVaultReferenceException(string message, Exception inner) : base (message, inner)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string Etag { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string SecretIdentifier { get; set; }
    }

}
