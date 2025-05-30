// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Implementation of ICdnCacheBustingAccessor that manages the current token for cache busting.
    /// </summary>
    internal class CdnCacheBustingAccessor : ICdnCacheBustingAccessor
    {
        private string _currentToken;

        /// <summary>
        /// Gets or sets the current token value to be used for cache busting.
        /// When null, CDN cache busting is disabled. When not null, the token will be injected into requests.
        /// </summary>
        public string CurrentToken
        {
            get => _currentToken;
            set => _currentToken = value;
        }
    }
}
