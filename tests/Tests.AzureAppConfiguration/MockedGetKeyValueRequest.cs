using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Tests.AzureAppConfiguration
{
    class MockedGetKeyValueRequest : HttpMessageHandler
    {
        private int _millisecondsDelay;
        private readonly IEnumerable<IKeyValue> _kvCollection;

        public int RequestCount { get; private set; } = 0;

        public MockedGetKeyValueRequest(IEnumerable<IKeyValue> kvCollection, int millisecondsDelay = 0)
        {
            _millisecondsDelay = millisecondsDelay;
            _kvCollection = kvCollection ?? throw new ArgumentException(nameof(kvCollection));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Thread.Sleep(_millisecondsDelay);
            HttpMethod method = request.Method;
            string pathAndQuery = request.RequestUri.PathAndQuery;

            if (request.Method == HttpMethod.Get)
            {
                if (pathAndQuery.StartsWith("/kv/?key="))
                {
                    NameValueCollection queryParams = HttpUtility.ParseQueryString(request.RequestUri.Query);
                    string keyFilter = queryParams["key"];

                    if (keyFilter.Contains("*"))
                    {
                        return GetKeyValuesResponse(_kvCollection);
                    }

                    IEnumerable<IKeyValue> keyValues = _kvCollection.Where(kv => kv.Key.Equals(keyFilter));
                    return GetKeyValuesResponse(keyValues);
                }
                else if (pathAndQuery.StartsWith("/kv/"))
                {
                    string[] segments = new Uri(request.RequestUri.AbsoluteUri).Segments;
                    string key = segments.Last();
                    IKeyValue keyValue = _kvCollection.Where(kv => kv.Key.Equals(key)).FirstOrDefault();
                    return GetKeyValueResponse(keyValue);
                }
            }

            return Task.FromResult(new HttpResponseMessage());
        }

        private Task<HttpResponseMessage> GetKeyValueResponse(IKeyValue kv)
        {
            var response = new HttpResponseMessage();
            response.StatusCode = HttpStatusCode.OK;

            string json = JsonConvert.SerializeObject(kv);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return Task.FromResult(response);
        }

        private Task<HttpResponseMessage> GetKeyValuesResponse(IEnumerable<IKeyValue> kvCollection)
        {
            var response = new HttpResponseMessage();
            response.StatusCode = HttpStatusCode.OK;

            string json = JsonConvert.SerializeObject(new { items = kvCollection });
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return Task.FromResult(response);
        }
    }
}
