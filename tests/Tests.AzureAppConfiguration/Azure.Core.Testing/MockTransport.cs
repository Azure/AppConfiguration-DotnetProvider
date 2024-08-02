﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.Core.Testing
{
    public class MockTransport : HttpPipelineTransport
    {
        private readonly Func<MockRequest, MockResponse> _responseFactory;

        public AsyncGate<MockRequest, MockResponse> RequestGate { get; }

        public List<MockRequest> Requests { get; } = new List<MockRequest>();

        public bool? ExpectSyncPipeline { get; set; }

        public MockTransport()
        {
            RequestGate = new AsyncGate<MockRequest, MockResponse>();
        }

        public MockTransport(params MockResponse[] responses)
        {
            var requestIndex = 0;
            _responseFactory = req => responses[requestIndex++];
        }

        public MockTransport(Func<MockRequest, MockResponse> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public override Request CreateRequest()
            => new MockRequest();

        public override void Process(HttpMessage message)
        {
            if (ExpectSyncPipeline == false)
            {
                throw new InvalidOperationException("Sync pipeline invocation not expected");
            }

            ProcessCore(message).GetAwaiter().GetResult();
        }

        public override async ValueTask ProcessAsync(HttpMessage message)
        {
            if (ExpectSyncPipeline == true)
            {
                throw new InvalidOperationException("Async pipeline invocation not expected");
            }

            await ProcessCore(message);
        }

        private async Task ProcessCore(HttpMessage message)
        {
            if (!(message.Request is MockRequest request))
                throw new InvalidOperationException("the request is not compatible with the transport");

            Requests.Add(request);

            if (RequestGate != null)
            {
                message.Response = await RequestGate.WaitForRelease(request);
            }
            else
            {
                message.Response = _responseFactory(request);
            }

            message.Response.ClientRequestId = request.ClientRequestId;

            if (message.Response.ContentStream != null && ExpectSyncPipeline != null)
            {
                message.Response.ContentStream = new AsyncValidatingStream(!ExpectSyncPipeline.Value, message.Response.ContentStream);
            }
        }

        public MockRequest SingleRequest => Requests.First();
    }
}
