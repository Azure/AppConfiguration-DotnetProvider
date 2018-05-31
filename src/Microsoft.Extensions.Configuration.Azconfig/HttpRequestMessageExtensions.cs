using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    static class HttpRequestMessageExtensions
    {
        public static HttpRequestMessage Sign(this HttpRequestMessage request, string credential, byte[] secret)
        {
            string host = request.RequestUri.Authority;
            string verb = request.Method.ToString().ToUpper();
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            string contentHash = Convert.ToBase64String(request.Content.ComputeSha256Hash());

            //
            // SignedHeaders
            string signedHeaders = "date;host;x-ms-content-sha256"; // Semicolon separated header names

            //
            // String-To-Sign
            var stringToSign = $"{verb}\n{request.RequestUri.PathAndQuery}\n{utcNow.ToString("r")};{host};{contentHash}";

            //
            // Signature
            string signature;

            using (var hmac = new HMACSHA256(secret))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(stringToSign)));
            }

            //
            // Add headers
            request.Headers.Date = utcNow;
            request.Headers.Add("x-ms-content-sha256", contentHash);
            request.Headers.Authorization = new AuthenticationHeaderValue("HMAC-SHA256", $"Credential={credential}, SignedHeaders={signedHeaders}, Signature={signature}");

            return request;
        }
    }

    static class HttpContentExtensions
    {
        public static byte[] ComputeSha256Hash(this HttpContent content)
        {
            using (var stream = new MemoryStream())
            {
                if (content != null)
                {
                    content.CopyToAsync(stream).Wait();
                    stream.Seek(0, SeekOrigin.Begin);
                }

                using (var alg = SHA256.Create())
                {
                    return alg.ComputeHash(stream.ToArray());
                }
            }
        }
    }
}
