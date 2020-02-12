// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class Utf8JsonReaderExtensions
    {
        public static string ReadAsString(this Utf8JsonReader reader)
        {
            if (reader.Read())
            {
                return reader.GetString();
            }

            return null;
        }
    }
}
