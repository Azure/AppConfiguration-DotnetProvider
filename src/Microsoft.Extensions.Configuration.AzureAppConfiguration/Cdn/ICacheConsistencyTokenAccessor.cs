// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    // <summary>
    // Interface for accessing the cache consistency token used when connecting to a CDN.
    // </summary>
    internal interface ICacheConsistencyTokenAccessor
    {
        /// <summary>
        /// Gets or sets the current token value to be used for cache consistency.
        /// When null, cache consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        string Current { get; set; }
    }
}
