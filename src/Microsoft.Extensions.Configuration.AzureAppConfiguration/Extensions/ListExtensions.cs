﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using DnsClient.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ListExtensions
    {
        public static List<T> Shuffle<T>(this List<T> values)
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

        public static List<SrvRecord> SortSrvRecords(this List<SrvRecord> srvRecords)
        {
            srvRecords.Sort((a, b) =>
            {
                if (a.Priority != b.Priority)
                    return a.Priority.CompareTo(b.Priority);

                if (a.Weight != b.Weight)
                    return b.Weight.CompareTo(a.Weight);

                return 0;
            });

            return srvRecords;
        }

        public static void AppendUnique<T>(this List<T> items, T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

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
