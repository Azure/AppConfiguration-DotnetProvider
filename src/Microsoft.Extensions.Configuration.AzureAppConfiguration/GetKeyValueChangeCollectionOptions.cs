// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class GetKeyValueChangeCollectionOptions
    {
        public string? KeyFilter { get; set; }
        public string? Label { get; set; }
        public bool RequestTracingEnabled { get; set; }
        public RequestTracingOptions? RequestTracingOptions { get; set; }
    }
}
