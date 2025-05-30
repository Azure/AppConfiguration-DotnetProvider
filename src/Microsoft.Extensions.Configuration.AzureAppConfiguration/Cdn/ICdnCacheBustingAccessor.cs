// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Provides access to CDN cache busting context for managing token injection into HTTP requests.
    /// </summary>
    internal interface ICdnCacheBustingAccessor
    {
        /// <summary>
        /// Gets or sets the current token value to be used for cache busting.
        /// When null, CDN cache busting is disabled. When not null, the token will be injected into requests.
        /// </summary>
        string CurrentToken { get; set; }
    }
}
