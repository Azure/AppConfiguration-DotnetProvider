// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    /// <summary>
    /// Implementation of ICacheConsistencyTokenAccessor that manages the current token for cache consistency.
    /// </summary>
    internal class CacheConsistencyTokenAccessor : ICacheConsistencyTokenAccessor
    {
        private string _currentToken;

        /// <summary>
        /// Gets or sets the current token value to be used for cache consistency.
        /// When null, cache consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        public string Current
        {
            get => _currentToken;
            set => _currentToken = value;
        }
    }
}
