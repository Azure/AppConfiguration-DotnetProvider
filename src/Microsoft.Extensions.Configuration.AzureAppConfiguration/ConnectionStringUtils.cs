// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class ConnectionStringUtils
    {
        public const string EndpointSection = "Endpoint";

        public const string SecretSection = "Secret";

        public const string IdSection = "Id";

        public static string Parse(string connectionString, string token)
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
                throw new FormatException("Invalid connection string format.");
            }

            var endIndex = connectionString.IndexOf(";", startIndex + parseToken.Length);
            if (endIndex < 0)
            {
                endIndex = connectionString.Length;
            }

            return connectionString.Substring(startIndex + parseToken.Length, endIndex - startIndex - parseToken.Length);
        }

        public static string Build(Uri endpoint, string id, string secret)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (string.IsNullOrEmpty(secret))
            {
                throw new ArgumentNullException(nameof(secret));
            }

            return $"{EndpointSection}={endpoint.AbsoluteUri.TrimEnd('/')};{IdSection}={id};{SecretSection}={secret}";
        }
    }
}
