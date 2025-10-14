// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    /// <summary>
    /// HTTP pipeline policy that removes Authorization and Sync-Token headers from outgoing requests.
    /// </summary>
    internal class AfdPolicy : HttpPipelinePolicy
    {
        /// <summary>
        /// Processes the HTTP message and removes Authorization and Sync-Token headers.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove(RequestHeaders.Authorization);

            message.Request.Headers.Remove(RequestHeaders.SyncToken);

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message and removes Authorization and Sync-Token headers.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove(RequestHeaders.Authorization);

            message.Request.Headers.Remove(RequestHeaders.SyncToken);

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }
    }
}
