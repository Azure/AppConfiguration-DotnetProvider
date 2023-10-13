// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ListExtensions
    {
        public static IEnumerable<T> Shuffle<T>(this IList<T> values)
        {
            var rdm = new Random();

            int length = values.Count;

            for (int i = length - 1; i >= 0; i--)
            {
                int swapIndex = rdm.Next(i + 1);

                yield return values[swapIndex];

                values[swapIndex] = values[i];
            }
        }
    }
}
