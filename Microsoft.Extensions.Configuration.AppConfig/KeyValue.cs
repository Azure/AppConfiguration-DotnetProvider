namespace Microsoft.Extensions.Configuration.AppConfig
{
    using System;

    class KeyValue : IKeyValue
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string ContentType { get; set; }
        public string ETag { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset LastModified { get; set; }
    }
}
