namespace Microsoft.Extensions.Configuration.AppConfig
{
    class KeyValueFormatter : IKeyValueFormatter
    {
        public string Format(IKeyValue keyValue)
        {
            return keyValue.Value;
        }
    }
}
