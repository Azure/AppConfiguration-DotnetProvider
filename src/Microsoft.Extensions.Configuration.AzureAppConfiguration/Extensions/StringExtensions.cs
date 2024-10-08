// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class LabelFilters
    {
        public static readonly string Null = "\0";

        public static readonly string Any = "*";
    }

    internal static class StringExtensions
    {
        public static string NormalizeNull(this string s)
        {
            return s == LabelFilters.Null ? null : s;
        }

        public static string ToBase64String(this string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);

            return Convert.ToBase64String(bytes);
        }
    }
}
