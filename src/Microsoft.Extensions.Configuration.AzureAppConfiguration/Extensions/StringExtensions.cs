namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class LabelFilters
    {
        public static readonly string Null = "\0";

        public static readonly string Any = "*";
    }

    internal static class StringExtensions
    {
        public static string NormalizeNull(this string s)
        {
            return s == LabelFilters.Null ? null : s;
        }
    }
}
