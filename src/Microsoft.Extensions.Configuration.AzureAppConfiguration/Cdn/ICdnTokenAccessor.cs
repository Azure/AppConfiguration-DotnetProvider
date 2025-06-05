// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    // <summary>
    // Interface for accessing the CDN cache breakage/consistency token.
    // </summary>
    internal interface ICdnTokenAccessor
    {
        /// <summary>
        /// Gets or sets the current token value to be used for CDN cache breakage/consistency.
        /// When null, CDN cache breakage/consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        string Current { get; set; }
    }
}
