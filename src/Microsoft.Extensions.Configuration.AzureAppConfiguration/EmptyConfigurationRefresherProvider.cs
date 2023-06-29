// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A dummy class used to show that <see cref="AzureAppConfigurationRefresherProvider"/> is not available.
    /// </summary>
    public class EmptyConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
        /// <summary>
        /// A dummy variable to implement the interface <see cref="IConfigurationRefresherProvider"/>.
        /// </summary>
        public IEnumerable<IConfigurationRefresher> Refreshers => new List<IConfigurationRefresher>();
    }
}
