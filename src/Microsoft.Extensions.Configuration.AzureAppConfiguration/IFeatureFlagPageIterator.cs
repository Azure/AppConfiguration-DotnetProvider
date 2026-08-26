// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IFeatureFlagPageIterator
    {
        IAsyncEnumerable<Page<FeatureFlag>> IteratePages(AsyncPageable<FeatureFlag> pageable);

        IAsyncEnumerable<Page<FeatureFlag>> IteratePages(AsyncPageable<FeatureFlag> pageable, IEnumerable<MatchConditions> matchConditions);
    }
}
