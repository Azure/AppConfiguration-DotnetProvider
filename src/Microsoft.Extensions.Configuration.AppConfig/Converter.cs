namespace Microsoft.Extensions.Configuration.AppConfig
{
    using Newtonsoft.Json.Linq;
    using System;

    class Converter
    {
        public KeyValue ToKeyValue(JObject obj)
        {
            var setting = new KeyValue();

            setting.Key = obj.Value<string>("key");
            setting.Value = obj.Value<string>("value");
            setting.ContentType = obj.Value<string>("content_type");
            setting.ETag = obj.Value<string>("etag");
            setting.Created = obj.Value<DateTime>("created");
            setting.LastModified = obj.Value<DateTime>("last_modified");

            return setting;
        }
    }
}
