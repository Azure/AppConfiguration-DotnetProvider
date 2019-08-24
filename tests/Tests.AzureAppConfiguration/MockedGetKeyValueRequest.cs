using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class MockedGetKeyValueRequest : HttpMessageHandler
    {
        private IKeyValue _kv;
        private int _millisecondsDelay;
        private readonly IEnumerable<IKeyValue> _kvCollection;

        public int RequestCount { get; private set; } = 0;

        public MockedGetKeyValueRequest(IKeyValue kv, IEnumerable<IKeyValue>  kvCollection, int millisecondsDelay = 0)
        {
            _kv = kv ?? throw new ArgumentException(nameof(kv));
            _millisecondsDelay = millisecondsDelay;
            _kvCollection = kvCollection ?? throw new ArgumentException(nameof(kvCollection));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Thread.Sleep(_millisecondsDelay);
            HttpMethod method = request.Method;

            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri.AbsolutePath.StartsWith($"/kv/NonExistentKey"))
                {
                    return GetNonExistentKeyValueResponse();
                }
                else if (request.RequestUri.AbsolutePath.StartsWith($"/kv/{_kv.Key}"))
                {
                    return GetKeyValueResponse();
                }
                else if (request.RequestUri.AbsolutePath.StartsWith($"/kv"))
                {
                    return GetKeyValuesResponse();
                }
            }

            return Task.FromResult(new HttpResponseMessage());
        }

        private Task<HttpResponseMessage> GetNonExistentKeyValueResponse()
        {
            var response = new HttpResponseMessage();
            response.StatusCode = HttpStatusCode.NotFound;
            response.Content = null;

            return Task.FromResult(response);
        }

        private Task<HttpResponseMessage> GetKeyValueResponse()
        {
            var response = new HttpResponseMessage();
            response.StatusCode = HttpStatusCode.OK;

            string json = JsonConvert.SerializeObject(_kv);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return Task.FromResult(response);
        }

        private Task<HttpResponseMessage> GetKeyValuesResponse()
        {
            var response = new HttpResponseMessage();
            response.StatusCode = HttpStatusCode.OK;

            string json = JsonConvert.SerializeObject(new { items = _kvCollection });
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return Task.FromResult(response);
        }
    }
}
