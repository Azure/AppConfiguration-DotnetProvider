// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Linq;
using System;
using System.Net.Mime;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ContentTypeExtensions
    {
        private static readonly IEnumerable<string> ExcludedJsonContentTypes = new[]
        {
            FeatureManagementConstants.ContentType,
            KeyVaultConstants.ContentType
        };

        public static bool IsAi(this ContentType contentType)
        {
            return contentType != null &&
                   contentType.IsJson() &&
                   contentType.Parameters.ContainsKey("profile") &&
                   !string.IsNullOrEmpty(contentType.Parameters["profile"]) &&
                   contentType.Parameters["profile"].StartsWith(RequestTracingConstants.AIMimeProfile);
        }

        public static bool IsAiChatCompletion(this ContentType contentType)
        {
            return contentType != null &&
                   contentType.IsJson() &&
                   contentType.Parameters.ContainsKey("profile") &&
                   !string.IsNullOrEmpty(contentType.Parameters["profile"]) &&
                   contentType.Parameters["profile"].StartsWith(RequestTracingConstants.AIChatCompletionMimeProfile);
        }

        public static bool IsJson(this ContentType contentType)
        {
            string acceptedMainType = "application";
            string acceptedSubType = "json";
            string mediaType = contentType.MediaType;

            if (!ExcludedJsonContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            {
                // Since contentType has been validated using System.Net.Mime.ContentType,
                // mediaType will always have exactly 2 parts after splitting on '/'
                string[] types = mediaType.Split('/');
                if (string.Equals(types[0], acceptedMainType, StringComparison.OrdinalIgnoreCase))
                {
                    string[] subTypes = types[1].Split('+');
                    if (subTypes.Contains(acceptedSubType, StringComparer.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
