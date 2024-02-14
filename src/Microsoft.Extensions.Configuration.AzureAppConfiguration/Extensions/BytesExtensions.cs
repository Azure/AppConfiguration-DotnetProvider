// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class BytesExtensions
    {
        /// <summary>
        /// Converts a byte array to Base64URL string and removes trailing '=' characters.
        /// Base64 description: https://datatracker.ietf.org/doc/html/rfc4648.html#section-4
        /// </summary>
        public static string ToBase64Url(this byte[] bytes)
        {
            string bytesBase64 = Convert.ToBase64String(bytes);

            int indexOfEquals = bytesBase64.IndexOf("=");

            // Remove all instances of "=" at the end of the string that were added as padding
            int stringBuilderCapacity = indexOfEquals != -1 ? indexOfEquals : bytesBase64.Length;

            StringBuilder stringBuilder = new StringBuilder(stringBuilderCapacity);

            // Construct Base64URL string by replacing characters in Base64 conversion that are not URL safe
            for (int i = 0; i < stringBuilderCapacity; i++)
            {
                if (bytesBase64[i] == '+')
                {
                    stringBuilder.Append('-');
                }
                else if (bytesBase64[i] == '/')
                {
                    stringBuilder.Append('_');
                }
                else
                {
                    stringBuilder.Append(bytesBase64[i]);
                }
            }

            return stringBuilder.ToString();
        }
    }
}
