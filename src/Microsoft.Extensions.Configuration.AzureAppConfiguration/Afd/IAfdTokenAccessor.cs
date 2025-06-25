// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    // <summary>
    // Interface for accessing the AFD cache breakage/consistency token.
    // </summary>
    internal interface IAfdTokenAccessor
    {
        /// <summary>
        /// Gets or sets the current token value to be used for AFD cache breakage/consistency.
        /// When null, AFD cache breakage/consistency is disabled. When not null, the token will be injected into requests.
        /// </summary>
        string Current { get; set; }
    }
}
