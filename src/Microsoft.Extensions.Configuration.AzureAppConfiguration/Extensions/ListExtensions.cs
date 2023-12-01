// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ListExtensions
    {

        public static void AppendUnique<T>(this List<T> items, T item)
        {
            T existingItem = items.FirstOrDefault(s => Equals(s, item));
            if (existingItem != null)
            {
                // Remove duplicate item if existing.
                items.Remove(existingItem);
            }
            // Append to the end, keeping precedence.
            items.Add(item);
        }
    }
}
