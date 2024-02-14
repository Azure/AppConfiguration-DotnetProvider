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
            string featureFlagIdBase64 = Convert.ToBase64String(bytes);

            int indexOfEquals = featureFlagIdBase64.IndexOf("=");

            int stringBuilderCapacity = indexOfEquals != -1 ? indexOfEquals : featureFlagIdBase64.Length;

            StringBuilder featureFlagIdBuilder = new StringBuilder(stringBuilderCapacity);

            for (int i = 0; i < stringBuilderCapacity; i++)
            {
                if (featureFlagIdBase64[i] == '+')
                {
                    featureFlagIdBuilder.Append('-');
                }
                else if (featureFlagIdBase64[i] == '/')
                {
                    featureFlagIdBuilder.Append('_');
                }
                else
                {
                    featureFlagIdBuilder.Append(featureFlagIdBase64[i]);
                }
            }

            return featureFlagIdBuilder.ToString();
        }
    }
}
