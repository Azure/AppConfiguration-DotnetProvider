// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Types of read requests.
    /// </summary>
    internal enum RequestType
    {
        /// <summary>
        /// Indicate read for app initialization/startup.
        /// </summary>
        Startup,

        /// <summary>
        /// Indicate watch/observe type request.
        /// </summary>
        Watch
    }
}
