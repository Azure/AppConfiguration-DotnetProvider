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
        RemoteConfigurationOptions _options;

        public AppConfigClient(RemoteConfigurationOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<IEnumerable<IKeyValue>> GetSettings(string appConfigUri, string prefix)
        {
            var start = await GetSettings(appConfigUri, prefix, null);

            return await start.GetAll();
        }

        public async Task<IKeyValue> GetSetting(string appConfigUri, string key)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(appConfigUri);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/kv/" + key);

                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { $"application/json; version=\"{_options.AcceptVersion}\"" }))
                {
                    //
                    // Throwing mechanism
                }

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    //
                    // Throwing mechanism
                }

                return new Converter().ToKeyValue(JObject.Parse(await response.Content.ReadAsStringAsync()));
            }
        }

        public async Task<string> GetETag(string appConfigUri, string key)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(appConfigUri);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, "/kv/" + key);

                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { "application/json", $"version=\"{_options.AcceptVersion}\"" }))
                {
                    //
                    // Throwing mechanism
                }

                HttpResponseMessage response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    //
                    // Throwing mechanism
                }

                return response.Headers.GetValues("etag").First().Trim('"');
            }
        }

        private async Task<Page<IKeyValue>> GetSettings(string appConfigUri, string prefix, Page<IKeyValue> previous)
        {
            if (previous == null)
            {
                previous = new Page<IKeyValue>();
            }

            string after = previous.Items.Count > 0 ? previous.Items[previous.Items.Count - 1].Key : string.Empty;

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(appConfigUri);

                string listUrl = "/kv?after=" + after;

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, listUrl);

                if (!request.Headers.TryAddWithoutValidation("Accept", new string[] { $"application/json; version=\"{_options.AcceptVersion}\"" }))
                {
                    //
                    // Throwing mechanism
                }

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
                    page.Next = () => GetSettings(appConfigUri, prefix, page);
                }

                return page;
            }
        }
    }
}
