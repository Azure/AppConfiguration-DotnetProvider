namespace Microsoft.Extensions.Configuration.AppConfig
{
    public interface IKeyValueFormatter
    {
        string Format(IKeyValue keyValue);
    }
}
