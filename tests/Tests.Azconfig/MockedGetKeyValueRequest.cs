using Microsoft.Azconfig.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Azconfig
{
    class MockedGetKeyValueRequest : IHttpClient
    {
        private IKeyValue _kv;
        private readonly IEnumerable<IKeyValue> _kvCollection;
        private int _counter = 0;

        public MockedGetKeyValueRequest(IKeyValue kv, IEnumerable<IKeyValue>  kvCollection)
        {
            _kv = kv ?? throw new ArgumentException(nameof(kv));
            _kvCollection = kvCollection ?? throw new ArgumentException(nameof(kvCollection));
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpMethod method = request.Method;
            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri.AbsolutePath.StartsWith($"/kv/{_kv.Key}"))
                {
                    return GetKeyValue(request);
                }
                else if (request.RequestUri.AbsolutePath.StartsWith($"/kv"))
                {
                    return GetKeyValues(request);
                }
            }
            return Task.FromResult(new HttpResponseMessage());
        }

        private Task<HttpResponseMessage> GetKeyValue(HttpRequestMessage request)
        {
            // use counter to switch retrieved key value
            // used in observe key tests
            if (_counter > 1)
            {
                _kv = new KeyValue(_kv.Key)
                {
                    ContentType = _kv.ContentType,
                    Label = _kv.Label,
                    Value = "newValue",
                };
            }
            HttpResponseMessage response = new HttpResponseMessage();

            response.StatusCode = HttpStatusCode.OK;
            string json = JsonConvert.SerializeObject(_kv);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            _counter++;

            return Task.FromResult(response);
        }

        private Task<HttpResponseMessage> GetKeyValues(HttpRequestMessage request)
        {

            HttpResponseMessage response = new HttpResponseMessage();

            response.StatusCode = HttpStatusCode.OK;
            string json = JsonConvert.SerializeObject(new { items = _kvCollection });

            response.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return Task.FromResult(response);
        }

        public void Dispose() { }
    }
}