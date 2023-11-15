// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Verify the domain name of the endpoint matches the certificate.
    /// </summary>
    internal static class DomainVerifier
    {
        private const int TlsPort = 443;
        private const string SubjectAltNameOid = "2.5.29.17";
        private const string DnsName = "DNS Name=";

        public static async Task<IEnumerable<string>> GetValidDomains(Uri originEndpoint, string srvHostName)
        {
            IEnumerable<string> validDomains = await GetSubjectAlternativeNames(srvHostName).ConfigureAwait(false);

            var domainList = new List<string>();
            
            foreach (string domain in validDomains)
            {
                if (originEndpoint.DnsSafeHost.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                {
                    domainList.Add(domain);
                }
            }

            return domainList;
        }

        private static async Task<IEnumerable<string>> GetSubjectAlternativeNames(string endpoint)
        {
            Debug.Assert(!string.IsNullOrEmpty(endpoint));

            // Initiate the connection, so it will download the server certificate
            using var client = new TcpClient(endpoint, TlsPort);
            using var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsClientAsync(endpoint).ConfigureAwait(false);

            X509Certificate serverCertificate = sslStream.RemoteCertificate;

            if (serverCertificate == null)
            {
                return Enumerable.Empty<string>();
            }

            using (var cert = new X509Certificate2(serverCertificate))
            {
                return GetDomainsFromSanExtension(cert);
            }
        }

        private static IEnumerable<string> GetDomainsFromSanExtension(X509Certificate2 cert)
        {
            var validDomains = new List<string>();

            X509Extension sanExtension = cert.Extensions[SubjectAltNameOid];

            if (sanExtension != null)
            {
                IEnumerable<string> formattedSanExtensions = sanExtension
                    .Format(true)
                    .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string formattedExtension in formattedSanExtensions)
                {
                    // Valid pattern should be "DNS Name=*.domain.com"
                    if (!formattedExtension.StartsWith(DnsName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string value = formattedExtension.Substring(DnsName.Length);

                    // Skip non-multi domain
                    if (!value.StartsWith("*."))
                    {
                        continue;
                    }

                    // .domain.com
                    string domain = value.Substring(1);

                    if (domain.Length > 1 && !validDomains.Contains(domain))
                    {
                        validDomains.Add(domain);
                    }
                }
            }

            return validDomains;
        }
    }
}