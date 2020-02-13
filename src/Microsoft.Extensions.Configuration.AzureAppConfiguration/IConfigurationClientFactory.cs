// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientFactory
    {
        ConfigurationClient CreateConfigurationClient(string connectionString, ConfigurationClientOptions clientOptions);
        ConfigurationClient CreateConfigurationClient(Uri endpoint, TokenCredential credential, ConfigurationClientOptions clientOptions);
    }
}
