// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class GetKeyValueChangeCollectionOptions
    {
        public SettingSelector Selector { get; set; }
        public IEnumerable<MatchConditions> MatchConditions { get; set; }
        public bool RequestTracingEnabled { get; set; }
        public RequestTracingOptions RequestTracingOptions { get; set; }
    }
}
