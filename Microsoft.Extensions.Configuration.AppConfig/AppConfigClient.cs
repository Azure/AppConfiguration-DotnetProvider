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
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(appConfigUri);

                string listUrl = string.IsNullOrEmpty(prefix) ? "/kv" : "/kv?key=" + prefix;

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

                var settings = new List<IKeyValue>();

                foreach (var item in jResponse.Value<JArray>("items").ToObject<IEnumerable<JObject>>())
                {
                    settings.Add(converter.ToKeyValue(item));
                }

                return settings;
            }
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
    }
}
