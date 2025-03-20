﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// A selector used to control what key-values are retrieved from Azure App Configuration.
    /// </summary>
    public class KeyValueSelector
    {
        /// <summary>
        /// A filter that determines the set of keys that are included in the configuration provider.
        /// </summary>
        /// <remarks>See the documentation for this provider for details on the format of filter expressions</remarks>
        public string KeyFilter { get; set; }

        /// <summary>
        /// A filter that determines what label to use when selecting key-values for the the configuration provider.
        /// </summary>
        public string LabelFilter { get; set; }

        /// <summary>
        /// The name of the Azure App Configuration snapshot to use when selecting key-values for the configuration provider.
        /// </summary>
        public string SnapshotName { get; set; }

        /// <summary>
        /// A filter that determines what tags to require when selecting key-values for the the configuration provider.
        /// </summary>
        public IEnumerable<string> TagsFilter { get; set; }

        /// <summary>
        /// A boolean that signifies whether this selector is intended to select feature flags.
        /// </summary>
        public bool IsFeatureFlagSelector { get; set; }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            if (obj is KeyValueSelector selector)
            {
                return KeyFilter == selector.KeyFilter
                    && LabelFilter == selector.LabelFilter
                    && SnapshotName == selector.SnapshotName
                    && (TagsFilter != null ? new HashSet<string>(TagsFilter).SetEquals(selector.TagsFilter) : selector.TagsFilter == null)
                    && IsFeatureFlagSelector == selector.IsFeatureFlagSelector;
            }

            return false;
        }

        /// <summary>
        /// Serves as the hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            int tagsFilterHash = 3;

            if (TagsFilter != null && TagsFilter.Any())
            {
                var sortedTags = new SortedSet<string>(TagsFilter);

                if (sortedTags.Any())
                {
                    // Concatenate tags into a single string with a delimiter
                    string tagsString = string.Join("|", sortedTags);

                    // Use SHA256 to generate a hash for the tags
                    using (var sha256 = System.Security.Cryptography.SHA256.Create())
                    {
                        byte[] hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(tagsString));

                        // Convert the first 4 bytes of the hash to an int
                        tagsFilterHash = System.BitConverter.ToInt32(hashBytes, 0);
                    }
                }
            }

            return (KeyFilter?.GetHashCode() ?? 0) ^
                   (LabelFilter?.GetHashCode() ?? 1) ^
                   (SnapshotName?.GetHashCode() ?? 2) ^
                   tagsFilterHash ^
                   (IsFeatureFlagSelector.GetHashCode());
        }
    }
}
