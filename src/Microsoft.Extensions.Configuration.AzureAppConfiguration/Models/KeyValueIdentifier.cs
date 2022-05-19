// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    internal struct KeyValueIdentifier
    {
        /// <summary>
        /// Key of the key-value in App Configuration.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value in App Configuration.
        /// </summary>
        public string Label { get; set; }

        public KeyValueIdentifier(string key, string label)
        {
            Key = key;
            Label = label.NormalizeNull();
        }

        public KeyValueIdentifier Clone()
        {
            return new KeyValueIdentifier()
            {
                Key = string.Copy(Key),
                Label = Label != null ? string.Copy(Label) : null
            };
        }

        public override bool Equals(object obj)
        {
            if (obj is KeyValueIdentifier keyLabel)
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
