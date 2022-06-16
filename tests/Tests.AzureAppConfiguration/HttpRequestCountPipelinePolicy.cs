// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    internal class HttpRequestCountPipelinePolicy : HttpPipelinePolicy
    {
        public int RequestCount { get; private set; }

        public HttpRequestCountPipelinePolicy()
        {
            RequestCount = 0;
        }

        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            RequestCount++;
            ProcessNext(message, pipeline);
        }

        public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            RequestCount++;
            return ProcessNextAsync(message, pipeline);
        }

        public void ResetRequestCount()
        {
            this.RequestCount = 0;
        }
    }
}
