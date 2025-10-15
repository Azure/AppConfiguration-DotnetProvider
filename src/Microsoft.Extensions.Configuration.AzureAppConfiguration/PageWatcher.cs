// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class PageWatcher
    {
        public MatchConditions Etag { get; set; }
        public DateTimeOffset LastUpdateTime { get; set; }
    }
}
