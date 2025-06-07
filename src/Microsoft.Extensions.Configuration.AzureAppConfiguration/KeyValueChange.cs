// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System.Security.Cryptography;
using System.Text;

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

        public string GetCdnToken()
        {
            string token;

            if (ChangeType == KeyValueChangeType.Deleted)
            {
                using SHA256 sha256 = SHA256.Create();
                token = sha256.ComputeHash(Encoding.UTF8.GetBytes($"ResourceDeleted\n{Previous.ETag}")).ToBase64Url();
            }
            else
            {
                token = Current.ETag.ToString();
            }

            return token;
        }
    }
}
