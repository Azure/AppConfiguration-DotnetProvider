// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class ClassicFeatureConditions
    {
        public List<ClassicClientFilter> ClientFilters { get; set; } = new List<ClassicClientFilter>();

        public string RequirementType { get; set; }
    }
}
