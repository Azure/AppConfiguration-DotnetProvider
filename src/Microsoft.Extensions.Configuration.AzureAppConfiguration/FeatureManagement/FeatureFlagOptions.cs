﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
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
        /// <summary>
        /// A collection of <see cref="KeyValueSelector"/>.
        /// </summary>
        internal List<KeyValueSelector> FeatureFlagSelectors = new List<KeyValueSelector>();

        /// <summary>
        /// The label that feature flags will be selected from.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// The time after which the cached values of the feature flags expire.  Must be greater than or equal to 1 second.
        /// </summary>
        public TimeSpan CacheExpirationInterval { get; set; } = RefreshConstants.DefaultFeatureFlagsCacheExpirationInterval;

        /// <summary>
        /// Forces feature flags and dynamic features to be written into the configuration system using the  
        /// feature management V2.0 schema introduced in Microsoft.FeatureManagement 3.0.0-preview library.
        /// </summary>
        public bool EnableFeatureManagementV2SchemaPreview { get; set; } = false;

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
        public FeatureFlagOptions Select(string featureFlagFilter, string labelFilter = LabelFilter.Null)
        {
            if (string.IsNullOrEmpty(featureFlagFilter))
            {
                throw new ArgumentNullException(nameof(featureFlagFilter));
            }

            if (featureFlagFilter.EndsWith(@"\*"))
            {
                throw new ArgumentException(@"Feature flag filter should not end with '\*'.", nameof(featureFlagFilter));
            }

            if (labelFilter == null)
            {
                labelFilter = LabelFilter.Null;
            }

            // Do not support * and , for label filter for now.
            if (labelFilter.Contains('*') || labelFilter.Contains(','))
            {
                throw new ArgumentException("The characters '*' and ',' are not supported in label filters.", nameof(labelFilter));
            }

            string featureFlagPrefix = FeatureManagementConstants.FeatureFlagMarker + featureFlagFilter;

            if (!FeatureFlagSelectors.Any(s => s.KeyFilter.Equals(featureFlagPrefix) && s.LabelFilter.Equals(labelFilter)))
            {
                FeatureFlagSelectors.Add(new KeyValueSelector
                {
                    KeyFilter = featureFlagPrefix,
                    LabelFilter = labelFilter
                });
            }

            return this;
        }
    }
}
