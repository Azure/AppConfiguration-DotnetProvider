// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class FeatureConditions
    {
        public List<ClientFilter> ClientFilters { get; set; } = new List<ClientFilter>();

        public string RequirementType { get; set; }
    }
}