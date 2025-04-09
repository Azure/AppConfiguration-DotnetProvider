// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An interface for Azure App Configuration health check.
    /// </summary>
    public interface IConfigurationHealthCheck : IHealthCheck
    {
    }
}
