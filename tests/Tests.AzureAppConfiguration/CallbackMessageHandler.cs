// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    internal class CallbackMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public CallbackMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
