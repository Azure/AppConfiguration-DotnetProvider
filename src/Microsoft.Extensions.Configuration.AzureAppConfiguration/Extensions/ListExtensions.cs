// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ListExtensions
    {
        public static IList<T> Shuffle<T>(this IList<T> values)
        {
            var rdm = new Random();
            int count = values.Count;

            for (int i = count - 1; i > 0; i--)
            {
                int swapIndex = rdm.Next(i + 1);

                if (swapIndex != i)
                {
                    T value = values[swapIndex];
                    values[swapIndex] = values[i];
                    values[i] = value;
                }
            }

            return values;
        }
    }
}
