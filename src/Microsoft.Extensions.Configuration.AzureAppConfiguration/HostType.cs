namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum HostType
    {
        None,

        AzureWebApp,

        AzureFunction,

        Kubernetes,

        IISExpress
    }
}
