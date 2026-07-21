// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClassicFeatureTelemetry
    {
        public bool Enabled { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
