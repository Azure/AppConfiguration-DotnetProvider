namespace Tests.AppConfig
{
    using Microsoft.Extensions.Configuration.AppConfig;
    using System;
    using System.Text;

    class KeyValueFormatter : IKeyValueFormatter
    {
        public string Format(IKeyValue keyValue)
        {
            if (string.Equals(keyValue.ContentType, "text/base64", StringComparison.OrdinalIgnoreCase))
            {
                return Encoding.Unicode.GetString(Convert.FromBase64String(keyValue.Value));
            }

            return keyValue.Value;
        }
    }
}
