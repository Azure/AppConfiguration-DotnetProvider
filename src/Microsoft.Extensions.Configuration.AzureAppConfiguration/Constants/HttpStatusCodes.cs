// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class HttpStatusCodes
    {
        // This constant is necessary because System.Net.HttpStatusCode.TooManyRequests is only available in netstandard2.1 and higher.
        public static readonly int TooManyRequests = 429;

        // Name resolution failure or socket exception yield this response code.
        public static readonly int NoResponse = 0;
    }
}