// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Net.Mime;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class StringExtensions
    {
        private static readonly IEnumerable<string> ExcludedJsonContentTypes = new[]
        {
            FeatureManagementConstants.ContentType,
            KeyVaultConstants.ContentType
        };

        public static bool IsJsonContentType(this string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            string acceptedMainType = "application";
            string acceptedSubType = "json";
            string mediaType;

            try
            {
                mediaType = new ContentType(contentType.Trim()).MediaType;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (IndexOutOfRangeException)
            {
                // Bug in System.Net.Mime.ContentType throws this if contentType is "xyz/"
                // https://github.com/dotnet/runtime/issues/39337
                return false;
            }

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

        public static string NormalizeNull(this string s)
        {
            return s == LabelFilter.Null ? null : s;
        }
    }
}
