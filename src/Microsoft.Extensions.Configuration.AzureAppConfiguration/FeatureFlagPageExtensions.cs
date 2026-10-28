// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class FeatureFlagPageExtensions
    {
        public static IAsyncEnumerable<Page<FeatureFlag>> AsPages(this AsyncPageable<FeatureFlag> pageable, IFeatureFlagPageIterator pageIterator)
        {
            //
            // Allow custom iteration
            if (pageIterator != null)
            {
                return pageIterator.IteratePages(pageable);
            }

            return pageable.AsPages();
        }

        public static IAsyncEnumerable<Page<FeatureFlag>> AsPages(this AsyncPageable<FeatureFlag> pageable, IFeatureFlagPageIterator pageIterator, IEnumerable<MatchConditions> matchConditions)
        {
            //
            // Allow custom iteration
            if (pageIterator != null)
            {
                return pageIterator.IteratePages(pageable, matchConditions);
            }

            return pageable.AsPages(matchConditions);
        }
    }
}
