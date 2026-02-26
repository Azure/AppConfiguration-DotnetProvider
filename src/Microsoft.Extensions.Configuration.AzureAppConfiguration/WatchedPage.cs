// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class WatchedPage
    {
        public MatchConditions MatchConditions { get; set; }
        public DateTimeOffset LastServerResponseTime { get; set; }
    }
}
