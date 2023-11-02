// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationStartupException : Exception
    {
        internal AzureAppConfigurationStartupException(Exception innerException, string message = "Failed to connect to any store or replica during startup.") : base(message, innerException)
        {
        }
    }
}
