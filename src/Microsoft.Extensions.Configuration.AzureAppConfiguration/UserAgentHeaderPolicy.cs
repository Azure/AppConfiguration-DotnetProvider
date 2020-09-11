// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Diagnostics;
using System.Reflection;
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

        internal static string GenerateUserAgent()
        {
            Assembly assembly = typeof(AzureAppConfigurationOptions).Assembly;
            string informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            return $"{assembly.GetName().Name}/{informationalVersion}";
        }

        private async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline, bool async)
        {
            message.Request.Headers.Add(RequestTracingConstants.UserAgentHeader, GenerateUserAgent());

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
