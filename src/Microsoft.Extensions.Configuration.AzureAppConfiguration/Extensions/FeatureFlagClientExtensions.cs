// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using AppConfigFeatureFlag = Azure.Data.AppConfiguration.FeatureFlag;
using AppConfigFeatureFlagSelector = Azure.Data.AppConfiguration.FeatureFlagSelector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class FeatureFlagClientExtensions
    {
        public static async Task<bool> HaveFeatureFlagsChanged(
            this IAppConfigurationClient client,
            Models.FeatureFlagSelector featureFlagSelector,
            IEnumerable<WatchedPage> pageWatchers,
            IFeatureFlagPageIterator pageIterator,
            CancellationToken cancellationToken)
        {
            if (pageWatchers == null)
            {
                throw new ArgumentNullException(nameof(pageWatchers));
            }

            if (featureFlagSelector == null)
            {
                throw new ArgumentNullException(nameof(featureFlagSelector));
            }

            var selector = new AppConfigFeatureFlagSelector
            {
                NameFilter = featureFlagSelector.NameFilter,
                LabelFilter = featureFlagSelector.LabelFilter
            };

            if (featureFlagSelector.TagFilters != null)
            {
                foreach (string tag in featureFlagSelector.TagFilters)
                {
                    selector.TagsFilter.Add(tag);
                }
            }

            AsyncPageable<AppConfigFeatureFlag> pageable = client.GetFeatureFlagsAsync(selector, cancellationToken);

            using IEnumerator<WatchedPage> existingPageWatcherEnumerator = pageWatchers.GetEnumerator();

            await foreach (Page<AppConfigFeatureFlag> page in pageable.AsPages(pageIterator, pageWatchers.Select(p => p.MatchConditions)).ConfigureAwait(false))
            {
                using Response rawResponse = page.GetRawResponse();
                DateTimeOffset serverResponseTime = rawResponse.GetMsDate();

                // Return true if the lists of etags are different
                if (!existingPageWatcherEnumerator.MoveNext() ||
                    (rawResponse.Status == (int)HttpStatusCode.OK &&
                    // if the server response time is later than last server response time, the change is considered detected
                    serverResponseTime >= existingPageWatcherEnumerator.Current.LastServerResponseTime &&
                    !existingPageWatcherEnumerator.Current.MatchConditions.IfNoneMatch.Equals(rawResponse.Headers.ETag)))
                {
                    return true;
                }
            }

            // Need to check if pages were deleted and no change was found within the new shorter list of pages
            return existingPageWatcherEnumerator.MoveNext();
        }
    }
}
