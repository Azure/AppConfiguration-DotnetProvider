namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum HostType
    {
        Unidentified,

        AzureWebApp,

        AzureFunction,

        Kubernetes,

        IISExpress
    }
}
