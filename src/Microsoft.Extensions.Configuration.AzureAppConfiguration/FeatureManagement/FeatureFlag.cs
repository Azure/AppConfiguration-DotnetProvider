// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureFlag
    {
        public string Id { get; set; }

        public bool Enabled { get; set; }

        public FeatureConditions Conditions { get; set; }
    }
}
