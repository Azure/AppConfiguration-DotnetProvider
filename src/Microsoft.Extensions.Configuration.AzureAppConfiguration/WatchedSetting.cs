// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class WatchedSetting
    {
        public ConfigurationSetting Setting { get; set; }
        public DateTimeOffset LastServerResponseTime { get; set; }
    }
}
