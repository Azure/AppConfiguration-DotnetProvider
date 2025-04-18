// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Net.Mime;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class StringExtensions
    {
        public static bool TryParseContentType(this string contentTypeString, out ContentType contentType)
        {
            contentType = null;

            if (string.IsNullOrWhiteSpace(contentTypeString))
            {
                return false;
            }

            try
            {
                contentType = new ContentType(contentTypeString.Trim());

                return true;
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
        }

        public static string NormalizeNull(this string s)
        {
            return s == LabelFilter.Null ? null : s;
        }
    }
}
