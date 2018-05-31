namespace Tests.Azconfig
{
    using Microsoft.Extensions.Configuration.Azconfig;
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
