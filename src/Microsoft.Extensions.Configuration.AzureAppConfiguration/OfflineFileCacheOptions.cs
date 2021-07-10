// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.DataProtection;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options for controlling the behavior of an <see cref="OfflineFileCache"/>.
    /// </summary>
    public class OfflineFileCacheOptions
    {
        /// <summary>
        /// The file path to use for persisting cached data.
        /// If your application is running in Azure App Service, storing the cache file in %HOME%  
        /// directory ensures that all instances of your app can access the same cache file.
        /// </summary>
        /// <remarks><see cref="Path"/> is mandatory.</remarks>
        public string Path { get; set; }

        /// <summary>
        /// An instance of <see cref="IDataProtector"/> to encrypt and decrypt cached data.
        /// If this option is not provided, a default <see cref="IDataProtector"/> instance will be created and used. 
        /// For more information on <see cref="IDataProtector"/> defaults, see 
        /// <see href="https://docs.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings"/>
        /// </summary>
        public IDataProtector DataProtector { get; set; }

        /// <summary>
        /// The maximum time upto which cached data can be used by the application.
        /// When new data is fetched from the server during startup or refresh operation, cached data and its expiration time will also be updated.
        /// For more information on lifetime of encrypted data, see 
        /// <see href="https://docs.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads"/>
        /// </summary>
        /// <remarks><see cref="Expiration"/> is mandatory.</remarks>
        public TimeSpan Expiration { get; set; }
    }
}
