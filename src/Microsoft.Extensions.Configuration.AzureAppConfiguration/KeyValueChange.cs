﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum KeyValueChangeType
    {
        None,
        Modified,
        Deleted
    }

    internal struct KeyValueChange
    {
        public KeyValueChangeType ChangeType { get; set; }

        public string Key { get; set; }

        public string Label { get; set; }

        public ConfigurationSetting Current { get; set; }

        public ConfigurationSetting Previous { get; set; }

        public DateTimeOffset DetectedTime { get; set; }
    }
}
