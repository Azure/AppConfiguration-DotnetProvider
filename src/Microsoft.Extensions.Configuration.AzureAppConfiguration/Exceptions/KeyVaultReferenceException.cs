// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// The exception that is thrown when there is an error resolving a reference to Azure Key Vault resource.
    /// </summary>
    public class KeyVaultReferenceException : Exception
    {
        private readonly string _message;

        /// <summary>
        /// Initializes a new instance of the System.Exception class with a specified error
        /// message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="inner">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.        /// </param>
        public KeyVaultReferenceException(string message,
                                           Exception inner)
         :base(string.Empty, inner)
        {
            _message = message;
        }

        /// <summary>
        ///Gets a message that describes the current exception.
        ///Returns The error message that explains the reason for the exception, or an empty string("").
        /// </summary>
        public override string  Message => $"{_message} ErrorCode: {ErrorCode}. Key: {Key}. Label: {Label}. Etag: {Etag}. SecretIdentifier: {SecretIdentifier}.";

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
