// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    /// <summary>
    /// Implementation of IAfdTokenAccessor that manages the current token for AFD cache breakage/consistency.
    /// </summary>
    internal class AfdTokenAccessor : IAfdTokenAccessor
    {
        private string _currentToken;

        /// <summary>
        /// Gets or sets the current token value to be used for AFD cache breakage/consistency.
        /// When null, AFD cache breakage/consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        public string Current
        {
            get => _currentToken;
            set => _currentToken = value;
        }
    }
}
