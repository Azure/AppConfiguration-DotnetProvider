// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Text;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class BytesExtensions
    {
        public static string ToBase64Url(this byte[] bytes)
        {
            string bytesBase64 = Convert.ToBase64String(bytes);

            int indexOfEquals = bytesBase64.IndexOf("=");

            int stringBuilderCapacity = indexOfEquals != -1 ? indexOfEquals : bytesBase64.Length;

            StringBuilder stringBuilder = new StringBuilder(stringBuilderCapacity);

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
