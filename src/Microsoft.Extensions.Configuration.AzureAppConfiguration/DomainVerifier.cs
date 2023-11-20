// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
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
                try
                {
                    return GetDomainsFromSanExtension(cert);
                }
                catch (AsnContentException)
                {
                    return Enumerable.Empty<string>();
                }
            }
        }

        private static IEnumerable<string> GetDomainsFromSanExtension(X509Certificate2 cert)
        {
            var validDomains = new List<string>();

            X509Extension sanExtension = cert.Extensions[SubjectAltNameOid];

            // For the DNS name, the tag class is ContextSpecific and tag value is 2 according to RFC 5280
            var dnsNameTag = new Asn1Tag(TagClass.ContextSpecific, tagValue: 2, isConstructed: false);

            AsnReader reader = new AsnReader(sanExtension.RawData, AsnEncodingRules.BER);
            AsnReader sequenceReader = reader.ReadSequence(Asn1Tag.Sequence);

            // Process each GeneralName
            while (sequenceReader.HasData)
            {
                Asn1Tag tag = sequenceReader.PeekTag();

                // Check if the current is a DNS name
                if (tag.Equals(dnsNameTag))
                {
                    // The domain name MUST be stored as an IA5String in subjectAltName extension according to RFC 5280
                    string dnsName = sequenceReader.ReadCharacterString(UniversalTagNumber.IA5String, dnsNameTag);

                    // Skip non-multi domain
                    if (dnsName.StartsWith("*."))
                    {
                        // .domain.com
                        string domain = dnsName.Substring(1);

                        if (domain.Length > 1 && !validDomains.Contains(domain))
                        {
                            validDomains.Add(domain);
                        }
                    }
                }
                else
                {
                    // Skip over non-DNSName types
                    sequenceReader.ReadOctetString(tag);
                }
            }

            return validDomains;
        }
    }
}