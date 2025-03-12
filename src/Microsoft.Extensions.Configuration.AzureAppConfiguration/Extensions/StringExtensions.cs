// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class StringExtensions
    {
        public static string NormalizeNull(this string s)
        {
            return s == LabelFilter.Null ? null : s;
        }
    }
}
