// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationStartupException : AggregateException
    {
        internal AzureAppConfigurationStartupException(string message, Exception innerException) : base(message, innerException)
        {
        }

        internal AzureAppConfigurationStartupException(string message, IEnumerable<Exception> innerExceptions) : base(message, innerExceptions)
        {
        }
    }
}
