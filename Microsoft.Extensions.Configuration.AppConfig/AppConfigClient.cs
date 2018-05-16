namespace Microsoft.Extensions.Configuration.AppConfig
{
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;

    class AppConfigClient : IAppConfigClient
    {
        private Uri _appConfigUri;
        private string _credential;
        private byte[] _secret;
        private RemoteConfigurationOptions _options;

        public AppConfigClient(string appConfigUri, string secretId, string secretValue, RemoteConfigurationOptions options)
        {
            if (string.IsNullOrWhiteSpace(appConfigUri))
            {
                throw new ArgumentNullException(nameof(appConfigUri));
            }
            if (secretValue == null)
            {
                throw new ArgumentNullException(nameof(secretValue));
            }

            _appConfigUri = new Uri(appConfigUri);
            _credential = secretId ?? throw new ArgumentNullException(nameof(secretId));
            _secret = Convert.FromBase64String(secretValue);
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IEnumerable<IKeyValue>> GetSettings(string prefix)
        {
            var start = await GetSettings(prefix, null);

            return await start.GetAll();
        }

        public async Task<IKeyValue> GetSetting(string key)
        {
            using (var client = new HttpClient())
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(_appConfigUri, "/kv/" + key));

                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { $"application/json" + (string.IsNullOrEmpty(_options.AcceptVersion) ? string.Empty : $"; version=\"{_options.AcceptVersion}\"") }))
                {
                    //
                    // Throwing mechanism
                }

                request.Sign(_credential, _secret);

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    //
                    // Throwing mechanism
                }

                return new Converter().ToKeyValue(JObject.Parse(await response.Content.ReadAsStringAsync()));
            }
        }

        public async Task<string> GetETag(string key)
        {
            using (var client = new HttpClient())
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, new Uri(_appConfigUri, "/kv/" + key));


                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { $"application/json" + (string.IsNullOrEmpty(_options.AcceptVersion) ? string.Empty : $"; version=\"{_options.AcceptVersion}\"") }))
                {
                    //
                    // Throwing mechanism
                }

                request.Sign(_credential, _secret);

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    //
                    // Throwing mechanism
                }

                return response.Headers.GetValues("etag").First().Trim('"');
            }
        }

        private async Task<Page<IKeyValue>> GetSettings(string prefix, Page<IKeyValue> previous)
        {
            if (previous == null)
            {
                previous = new Page<IKeyValue>();
            }

            string after = previous.Items.Count > 0 ? previous.Items[previous.Items.Count - 1].Key : string.Empty;

            using (var client = new HttpClient())
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(_appConfigUri, "/kv?after=" + after));

                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { $"application/json" + (string.IsNullOrEmpty(_options.AcceptVersion) ? string.Empty : $"; version=\"{_options.AcceptVersion}\"") }))
                {
                    //
                    // Throwing mechanism
                }

                request.Sign(_credential, _secret);

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    //
                    // Throwing mechanism
                }

                var converter = new Converter();

                JObject jResponse = JObject.Parse(await response.Content.ReadAsStringAsync());

                var page = new Page<IKeyValue>();

                foreach (var item in jResponse.Value<JArray>("items").ToObject<IEnumerable<JObject>>())
                {
                    var kv = converter.ToKeyValue(item);

                    if (!string.IsNullOrEmpty(prefix) && !kv.Key.StartsWith(prefix))
                    {
                        continue;
                    }

                    page.Items.Add(kv);
                }

                //
                // If the number of items is greater than 0 we must setup the page to request the next set of items
                // If there are no items then the last page was reached
                if (page.Items.Count > 0)
                {
                    page.Next = () => GetSettings(prefix, page);
                }

                return page;
            }
        }
    }
}
