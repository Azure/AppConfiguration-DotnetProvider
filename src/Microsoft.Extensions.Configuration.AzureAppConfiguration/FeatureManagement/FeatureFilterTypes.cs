// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    /// <summary>
    /// Types of built-in feature filters.
    /// </summary>
    [Flags]
    internal enum FeatureFilterType
    {
        None = 0,
        Custom = 1,
        Percent = 2,
        Time = 4,
        Target = 8
    }
}
