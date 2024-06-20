// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Azure.Core.Testing
{
    public class MockResponse : Response
    {
        private readonly Dictionary<string, List<string>> _headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public MockResponse(int status, string reasonPhrase = null)
        {
            Status = status;
            ReasonPhrase = reasonPhrase;
        }

        public override int Status { get; }

        public override string ReasonPhrase { get; }

        public override Stream ContentStream { get; set; }

        public override string ClientRequestId { get; set; }

        public bool IsDisposed { get; private set; }

        public override string ToString() => $"{Status}";

        public void SetContent(byte[] content)
        {
            ContentStream = new MemoryStream(content);
        }

        public void SetContent(string content)
        {
            SetContent(Encoding.UTF8.GetBytes(content));
        }

        public void AddHeader(HttpHeader header)
        {
            if (!_headers.TryGetValue(header.Name, out List<string> values))
            {
                _headers[header.Name] = values = new List<string>();
            }

            values.Add(header.Value);
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            if (_headers.TryGetValue(name, out List<string> values))
            {
                value = JoinHeaderValue(values);
                return true;
            }

            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            var result = _headers.TryGetValue(name, out List<string> valuesList);
            values = valuesList;
            return result;
        }

        protected override bool ContainsHeader(string name)
        {
            return TryGetHeaderValues(name, out _);
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => _headers.Select(h => new HttpHeader(h.Key, JoinHeaderValue(h.Value)));

        private static string JoinHeaderValue(IEnumerable<string> values)
        {
            return string.Join(",", values);
        }

        public override void Dispose()
        {
            IsDisposed = true;
        }
    }

    class MockResponse<T> : Response<T>
    {
        private T _value;
        public override T Value => _value;

        public MockResponse(T value)
        {
            _value = value;
        }

        public override Response GetRawResponse()
        {
            var response = new MockResponse(200);

            response.AddHeader(new HttpHeader("ETag", new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1").ToString()));

            throw new NotImplementedException();
        }
    }
}
