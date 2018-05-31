namespace Tests.Azconfig
{
    using Microsoft.Extensions.Configuration.Azconfig;
    using System;
    using System.Text;
    using System.Security.Cryptography;

    class KeyValue : IKeyValue
    {
        private string _value;

        public KeyValue()
        {
            Created = DateTime.UtcNow;
            LastModified = DateTime.UtcNow;

            SetETag();
        }

        public string Key { get; set; }

        public string Value {

            get 
            {
                return _value;
            }

            set 
            {
                _value = value;
                LastModified = DateTime.UtcNow;
                SetETag();
            }
        }

        public string ContentType { get; set; }

        public string ETag { get; set; }

        public DateTimeOffset Created { get; set; }

        public DateTimeOffset LastModified { get; set; }

        private void SetETag()
        {
            using (SHA256 algo = SHA256.Create())
            {
                ETag = BitConverter.ToString(algo.ComputeHash(Encoding.Unicode.GetBytes(LastModified.ToString("o")))).Substring(0, 32);
            }
        }

        public static KeyValue Clone(IKeyValue kv)
        {
            return new KeyValue()
            {
                Key = kv.Key,
                Value = kv.Value,
                ContentType = kv.ContentType,
                Created = kv.Created,
                LastModified = kv.LastModified,
                ETag = kv.ETag
            };
        }
    }
}
