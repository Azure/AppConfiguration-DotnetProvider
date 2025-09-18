// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Diagnostics;
using System.Web;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    /// <summary>
    /// HTTP pipeline policy that removes Authorization headers from requests.
    /// </summary>
    internal class AfdPolicy : HttpPipelinePolicy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AfdPolicy"/> class.
        /// </summary>
        public AfdPolicy()
        {
        }

        /// <summary>
        /// Processes the HTTP message and removes the Authorization header from the request.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove("Authorization");

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously and removes the Authorization header from the request.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove("Authorization");

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }
    }
}
