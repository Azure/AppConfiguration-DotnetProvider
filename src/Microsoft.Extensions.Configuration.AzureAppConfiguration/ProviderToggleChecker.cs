// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;
using System.Security;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class ProviderToggleChecker
    {
        public static bool IsProviderDisabled()
        {
            try
            {
                return bool.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariableNames.AppConfigurationProviderDisabled), out bool disabled) ? disabled : false;
            }
            catch (SecurityException) { }

            return false;
        }
    }
}
