// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;
using System.Security;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class Utils
    {
        public static bool IsProviderDisabled()
        {
            try
            {
                return bool.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariables.DisableAppConfigurationProvider), out bool disabled) ? disabled : false;
            }
            catch (SecurityException) { }

            return false;
        }
    }
}
