using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class StringExtensions
    {
        public static string NormalizeNull(this string s)
        {
            return s == LabelFilters.Null ? null : s;
        }
    }
}
