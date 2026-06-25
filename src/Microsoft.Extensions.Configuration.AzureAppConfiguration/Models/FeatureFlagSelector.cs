// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// A selector used to control what feature flags are retrieved from Azure App Configuration.
    /// A single feature flag selector is used to query both classic feature flags (key-values prefixed
    /// with ".appconfig.featureflag/") and feature flags returned by the standalone feature-flag endpoint.
    /// </summary>
    internal class FeatureFlagSelector
    {
        /// <summary>
        /// A filter that determines the set of feature flag names that are included in the configuration provider.
        /// The name filter does not include the ".appconfig.featureflag/" prefix.
        /// </summary>
        public string NameFilter { get; set; }

        /// <summary>
        /// A filter that determines what label to use when selecting feature flags for the configuration provider.
        /// </summary>
        public string LabelFilter { get; set; }

        /// <summary>
        /// A filter that determines what tags to require when selecting feature flags for the configuration provider.
        /// </summary>
        public IEnumerable<string> TagFilters { get; set; }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            if (obj is FeatureFlagSelector selector)
            {
                return NameFilter == selector.NameFilter
                    && LabelFilter == selector.LabelFilter
                    && (TagFilters == null
                            ? selector.TagFilters == null
                            : selector.TagFilters != null && new HashSet<string>(TagFilters).SetEquals(selector.TagFilters));
            }

            return false;
        }

        /// <summary>
        /// Serves as the hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            string tagFiltersString = string.Empty;

            if (TagFilters != null && TagFilters.Any())
            {
                var sortedTags = new SortedSet<string>(TagFilters);

                // Concatenate tags into a single string with a delimiter
                tagFiltersString = string.Join("\n", sortedTags);
            }

            return HashCode.Combine(
                NameFilter,
                LabelFilter,
                tagFiltersString);
        }
    }
}
