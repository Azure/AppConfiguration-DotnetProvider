// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum HostType
    {
        Unidentified,

        AzureWebApp,

        AzureFunction,

        Kubernetes,

        IISExpress,

        ServiceFabric
    }
}
