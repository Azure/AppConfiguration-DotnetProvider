// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    internal struct KeyLabel
    {
        /// <summary>
        /// Key of the key-value in App Configuration.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value in App Configuration.
        /// </summary>
        public string Label { get; set; }

        public KeyLabel(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public override bool Equals(object obj)
        {
            if (obj is KeyLabel keyLabel)
            {
                return Key == keyLabel.Key && Label == keyLabel.Label;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Label != null ? Key.GetHashCode() ^ Label.GetHashCode() : Key.GetHashCode();
        }
    }
}
