// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class UserAgentHeaderPolicy : HttpPipelinePolicy
    {
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            ProcessAsync(message, pipeline, false).EnsureCompleted();
        }

        public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            return ProcessAsync(message, pipeline, true);
        }

        private async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline, bool async)
        {
            string headerValue = message.Request.Headers.TryGetValue(RequestTracingConstants.UserAgentHeader, out string sdkUserAgent)
                ? TracingUtils.GenerateUserAgent(sdkUserAgent) : TracingUtils.GenerateUserAgent();
            message.Request.Headers.SetValue(RequestTracingConstants.UserAgentHeader, headerValue);

            if (async)
            {
                await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
            }
            else
            {
                ProcessNext(message, pipeline);
            }
        }
    }

    internal static class TaskExtensions
    {
        public static void EnsureCompleted(this ValueTask task)
        {
            Debug.Assert(task.IsCompleted);
            task.GetAwaiter().GetResult();
        }
    }
}
