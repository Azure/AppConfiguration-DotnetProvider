// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Options used to configure feature flag loading via
    /// <see cref="AzureAppConfigurationOptions.ConfigureFeatureFlags"/>, the new feature flag
    /// experience offered by Azure App Configuration.
    /// </summary>
    /// <remarks>
    /// Draft name. Will be renamed prior to GA of the new feature flag experience.
    /// In contrast to <see cref="FeatureFlagOptions"/>, this options type is fully opt-in:
    /// <see cref="Enabled"/> must be set to <c>true</c> for any feature flags to be loaded, and
    /// refresh must be enabled separately via <see cref="ConfigureRefresh"/>.
    /// </remarks>
    public class FeatureFlagOptions2
    {
        /// <summary>
        /// Whether feature flag loading is enabled for this provider. Defaults to <c>false</c>;
        /// the new API requires the caller to opt in to feature flag loading explicitly.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// The label that feature flags will be selected from when no explicit
        /// <see cref="Select(string, string, IEnumerable{string})"/> calls are made. Mutually
        /// exclusive with <see cref="Select(string, string, IEnumerable{string})"/>.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// A collection of selectors describing which feature flags to load.
        /// </summary>
        internal List<KeyValueSelector> FeatureFlagSelectors { get; } = new List<KeyValueSelector>();

        /// <summary>
        /// Refresh options configured via <see cref="ConfigureRefresh"/>. <c>null</c> if refresh
        /// has not been configured.
        /// </summary>
        internal FeatureFlagRefreshOptions2 Refresh { get; private set; }

        /// <summary>
        /// Specifies the feature flags to include in the configuration provider.
        /// <see cref="Select(string, string, IEnumerable{string})"/> can be called multiple times
        /// to include multiple sets of feature flags.
        /// </summary>
        /// <param name="featureFlagFilter">
        /// The filter to apply to feature flag names. An asterisk (*) may be added to the end to
        /// match by prefix. The characters asterisk (*), comma (,), and backslash (\) are
        /// reserved and must be escaped with a backslash (\).
        /// </param>
        /// <param name="labelFilter">
        /// The label filter to apply. Defaults to the null label. The characters asterisk (*) and
        /// comma (,) are not supported.
        /// </param>
        /// <param name="tagFilters">
        /// Optional tag filters of the form "tagName=tagValue". Up to five tag filters may be
        /// supplied.
        /// </param>
        public FeatureFlagOptions2 Select(string featureFlagFilter = KeyFilter.Any, string labelFilter = LabelFilter.Null, IEnumerable<string> tagFilters = null)
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

            if (labelFilter.Contains("*") || labelFilter.Contains(","))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            if (tagFilters != null)
            {
                foreach (string tag in tagFilters)
                {
                    if (string.IsNullOrEmpty(tag) || !tag.Contains("=") || tag.IndexOf('=') == 0)
                    {
                        throw new ArgumentException($"Tag filter '{tag}' does not follow the format \"tagName=tagValue\".", nameof(tagFilters));
                    }
                }
            }

            string featureFlagPrefix = FeatureManagementConstants.FeatureFlagMarker + featureFlagFilter;

            FeatureFlagSelectors.AppendUnique(new KeyValueSelector
            {
                KeyFilter = featureFlagPrefix,
                LabelFilter = labelFilter,
                TagFilters = tagFilters,
                IsFeatureFlagSelector = true
            });

            return this;
        }

        /// <summary>
        /// Configures refresh behavior for the feature flags loaded by this provider.
        /// </summary>
        /// <param name="configure">A callback used to configure refresh options.</param>
        public FeatureFlagOptions2 ConfigureRefresh(Action<FeatureFlagRefreshOptions2> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var refreshOptions = new FeatureFlagRefreshOptions2();
            configure(refreshOptions);
            Refresh = refreshOptions;

            return this;
        }
    }
}
