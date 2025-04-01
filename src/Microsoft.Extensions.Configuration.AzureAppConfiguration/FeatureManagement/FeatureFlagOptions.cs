// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Options used to configure the feature flags to be parsed.
    /// </summary>
    public class FeatureFlagOptions
    {
        private TimeSpan _refreshInterval = RefreshConstants.DefaultFeatureFlagRefreshInterval;

        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        internal List<KeyValueSelector> FeatureFlagSelectors = new List<KeyValueSelector>();

        /// <summary>
        /// The time after which feature flags can be refreshed.  Must be greater than or equal to 1 second.
        /// </summary>
        internal TimeSpan RefreshInterval
        {
            get { return _refreshInterval; }
            set { _refreshInterval = value; }
        }

        /// <summary>
        /// The label that feature flags will be selected from.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The time after which the cached values of the feature flags expire.  Must be greater than or equal to 1 second.
        /// </summary>
        [Obsolete("The " + nameof(CacheExpirationInterval) + " property is deprecated and will be removed in a future release. " +
            "Please use the new " + nameof(SetRefreshInterval) + " method instead. " +
            "Note that the usage has changed, but the functionality remains the same.")]
        public TimeSpan CacheExpirationInterval
        {
            get { return _refreshInterval; }
            set { _refreshInterval = value; }
        }

        /// <summary>
        /// Sets the time after which feature flags can be refreshed.
        /// </summary>
        /// <param name="refreshInterval">
        /// Sets the minimum time interval between consecutive refresh operations for feature flags. Default value is 30 seconds. Must be greater than or equal to 1 second.
        /// </param>
        public FeatureFlagOptions SetRefreshInterval(TimeSpan refreshInterval)
        {
            RefreshInterval = refreshInterval;

            return this;
        }

        /// <summary>
        /// Specify what feature flags to include in the configuration provider.
        /// <see cref="Select"/> can be called multiple times to include multiple sets of feature flags.
        /// </summary>
        /// <param name="featureFlagFilter">
        /// The filter to apply to feature flag names when querying Azure App Configuration for feature flags.
        /// For example, you can select all feature flags that begin with "MyApp" by setting the featureflagFilter to "MyApp*". 
        /// The characters asterisk (*), comma (,) and backslash (\) are reserved and must be escaped using a backslash (\).
        /// Built-in feature flag filter options: <see cref="KeyFilter"/>.
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply when querying Azure App Configuration for feature flags. By default the null label will be used. Built-in label filter options: <see cref="LabelFilter"/>
        /// The characters asterisk (*) and comma (,) are not supported. Backslash (\) character is reserved and must be escaped using another backslash (\).
        /// </param>
        /// <param name="tagsFilters">
        /// The tag filter to apply when querying Azure App Configuration for key-values. By default no tags will be used.
        /// Each tag provided must follow the format "tagName=tagValue". A key-value will only be returned if its tags contain all tags provided
        /// in the filter, or if the filter is empty.
        /// The characters asterisk (*), comma (,) and backslash (\) are reserved and must be escaped using a backslash (\).
        /// </param>
        public FeatureFlagOptions Select(string featureFlagFilter, string labelFilter = LabelFilter.Null, IEnumerable<string> tagsFilters = null)
        {
            if (string.IsNullOrEmpty(featureFlagFilter))
            {
                throw new ArgumentNullException(nameof(featureFlagFilter));
            }

            if (featureFlagFilter.EndsWith(@"\*"))
            {
                throw new ArgumentException(@"Feature flag filter should not end with '\*'.", nameof(featureFlagFilter));
            }

            if (string.IsNullOrWhiteSpace(labelFilter))
            {
                labelFilter = LabelFilter.Null;
            }

            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            if (tagsFilters != null)
            {
                foreach (var tag in tagsFilters)
                {
                    if (string.IsNullOrEmpty(tag) || !tag.Contains('=') || tag.IndexOf('=') == 0)
                    {
                        throw new ArgumentException($"Tag filter '{tag}' does not follow the format \"tagName=tagValue\".", nameof(tagsFilters));
                    }
                }
            }

            string featureFlagPrefix = FeatureManagementConstants.FeatureFlagMarker + featureFlagFilter;

            FeatureFlagSelectors.AppendUnique(new KeyValueSelector
            {
                KeyFilter = featureFlagPrefix,
                LabelFilter = labelFilter,
                TagsFilters = tagsFilters,
                IsFeatureFlagSelector = true
            });

            return this;
        }
    }
}
