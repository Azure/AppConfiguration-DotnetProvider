// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Linq;
using System;
using System.Net.Mime;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReferences;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ContentTypeExtensions
    {
        public static bool IsAi(this ContentType contentType)
        {
            return contentType != null &&
                   contentType.IsJson() &&
                   !contentType.IsFeatureFlag() &&
                   !contentType.IsKeyVaultReference() &&
                   contentType.Parameters.ContainsKey("profile") &&
                   !string.IsNullOrEmpty(contentType.Parameters["profile"]) &&
                   contentType.Parameters["profile"].StartsWith(RequestTracingConstants.AIMimeProfile);
        }

        public static bool IsAiChatCompletion(this ContentType contentType)
        {
            return contentType != null &&
                   contentType.IsJson() &&
                   !contentType.IsFeatureFlag() &&
                   !contentType.IsKeyVaultReference() &&
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

            ReadOnlySpan<char> mediaTypeSpan = contentType.MediaType.AsSpan();

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

            return false;
        }

        public static bool IsFeatureFlag(this ContentType contentType)
        {
            return contentType.MediaType.Equals(FeatureManagementConstants.ContentType);
        }

        public static bool IsKeyVaultReference(this ContentType contentType)
        {
            return contentType.MediaType.Equals(KeyVaultConstants.ContentType);
        }

        public static bool IsSnapshotReference(this ContentType contentType)
        {
            return contentType.MediaType.Equals(SnapshotReferenceConstants.ContentType);
        }
    }
}