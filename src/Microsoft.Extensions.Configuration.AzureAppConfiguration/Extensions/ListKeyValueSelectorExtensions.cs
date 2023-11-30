// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class ListKeyValueSelectorExtensions
    {
        public static void AppendUnique(this List<KeyValueSelector> selectors, string keyFilter, string labelFilter)
        {
            KeyValueSelector existingKvSelector = selectors.FirstOrDefault(s => string.Equals(s.KeyFilter, keyFilter) && string.Equals(s.LabelFilter, labelFilter));

            if (existingKvSelector != null)
            {
                // Move to the end, keeping precedence.
                selectors.Remove(existingKvSelector);
                selectors.Add(existingKvSelector);
            }
            else
            {
                selectors.Add(new KeyValueSelector
                {
                    KeyFilter = keyFilter,
                    LabelFilter = labelFilter
                });
            }

        }
    }
}
