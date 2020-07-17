// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An interface used to retrieve refresher instances for App Configuration.
    /// </summary>
    public interface IConfigurationRefresherProvider
    {
        /// <summary>
        /// List of instances of <see cref="IConfigurationRefresher"/> for App Configuration.
        /// </summary>
        IEnumerable<IConfigurationRefresher> Refreshers { get; }
    }
}
