// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    /// <summary>
    /// Implementation of ICdnTokenAccessor that manages the current token for CDN cache breakage/consistency.
    /// </summary>
    internal class CdnTokenAccessor : ICdnTokenAccessor
    {
        private string _currentToken;

        /// <summary>
        /// Gets or sets the current token value to be used for CDN cache breakage/consistency.
        /// When null, CDN cache breakage/consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        public string Current
        {
            get => _currentToken;
            set => _currentToken = value;
        }
    }
}
