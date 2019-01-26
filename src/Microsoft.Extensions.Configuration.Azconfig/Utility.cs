using System;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    internal static class Utility
    {
        public static string ParseConnectionString(string connectionString, string token)
        {
            if (connectionString == null)
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            string parseToken = token + "=";
            var startIndex = connectionString.IndexOf(parseToken);
            if (startIndex < 0)
            {
                throw new ArgumentException("Invalid connection string format.");
            }

            var endIndex = connectionString.IndexOf(";", startIndex + parseToken.Length);
            if (endIndex < 0)
            {
                endIndex = connectionString.Length;
            }

            return connectionString.Substring(startIndex + parseToken.Length, endIndex - startIndex - parseToken.Length);
        }
    }
}
