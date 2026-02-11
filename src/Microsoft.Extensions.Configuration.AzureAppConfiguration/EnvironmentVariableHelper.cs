// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;
using System.Security;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class EnvironmentVariableHelper
    {
        public static bool GetBoolOrDefault(string variableName)
        {
            try
            {
                return bool.TryParse(Environment.GetEnvironmentVariable(variableName), out bool disabled) ? disabled : false;
            }
            catch (SecurityException) { }

            return false;
        }
    }
}
