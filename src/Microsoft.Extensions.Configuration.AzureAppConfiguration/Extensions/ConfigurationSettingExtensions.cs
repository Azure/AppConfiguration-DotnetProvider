// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System.Net.Mime;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ConfigurationSettingExtensions
    {
        public static bool IsKeyVaultReference(this ConfigurationSetting setting)
        {
            return setting != null
                && setting.ContentType.TryParseContentType(out ContentType contentType)
                && contentType.IsKeyVaultReference();
        }
    }
}
