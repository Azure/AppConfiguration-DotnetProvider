// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json.Serialization;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class Notification
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("etag")]
        public string Etag { get; set; }

        [JsonPropertyName("syncToken")]
        public string SyncToken { get; set; }
    }
}
