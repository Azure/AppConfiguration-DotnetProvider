// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// Holds the watched pages associated with a single <see cref="FeatureFlagSelector"/>.
    /// A feature flag selector tracks two collections of pages: one for classic feature flags loaded from the
    /// ".appconfig.featureflag/" key-value namespace and one for feature flags loaded from the standalone
    /// feature-flag endpoint.
    /// </summary>
    internal class FeatureFlagWatchedPages
    {
        /// <summary>
        /// The watched pages for classic feature flags. Null when classic feature flags are excluded.
        /// </summary>
        public IEnumerable<WatchedPage> ClassicPages { get; set; }

        /// <summary>
        /// The watched pages for feature flags loaded from the standalone feature-flag endpoint.
        /// </summary>
        public IEnumerable<WatchedPage> NewPages { get; set; }
    }
}
