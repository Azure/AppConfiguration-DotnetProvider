// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class EmptyConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
        public IEnumerable<IConfigurationRefresher> Refreshers => Enumerable.Empty<IConfigurationRefresher>();
    }
}
