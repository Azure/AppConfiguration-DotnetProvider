// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    internal struct KeyLabelIdentifier
    {
        /// <summary>
        /// Key of the key-value in App Configuration.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value in App Configuration.
        /// </summary>
        public string Label { get; set; }

        public KeyLabelIdentifier(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override bool Equals(object obj)
        {
            if (obj is KeyLabelIdentifier keyLabel)
            {
                return string.Equals(Key, keyLabel.Key, StringComparison.Ordinal)
                    && string.Equals(Label, keyLabel.Label, StringComparison.Ordinal);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Label != null ? Key.GetHashCode() ^ Label.GetHashCode() : Key.GetHashCode();
        }
    }
}
