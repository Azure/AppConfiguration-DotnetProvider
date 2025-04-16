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
            if (contentType == null)
            {
                return false;
            }

            string acceptedMainType = "application";
            string acceptedSubType = "json";
            string mediaType = contentType.MediaType;

            if (!ExcludedJsonContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            {
                ReadOnlySpan<char> mediaTypeSpan = mediaType.AsSpan();

                // Since contentType has been validated using System.Net.Mime.ContentType,
                // mediaType will always have exactly 2 parts after splitting on '/'
                int slashIndex = mediaTypeSpan.IndexOf('/');

                if (mediaTypeSpan.Slice(0, slashIndex).Equals(acceptedMainType.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> subTypeSpan = mediaTypeSpan.Slice(slashIndex + 1);

                    while (!subTypeSpan.IsEmpty)
                    {
                        int plusIndex = subTypeSpan.IndexOf('+');

                        ReadOnlySpan<char> currentSubType = plusIndex == -1 ? subTypeSpan : subTypeSpan.Slice(0, plusIndex);

                        if (currentSubType.Equals(acceptedSubType.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        subTypeSpan = plusIndex == -1 ? ReadOnlySpan<char>.Empty : subTypeSpan.Slice(plusIndex + 1);
                    }
                }
            }

            return false;
        }
    }
}
