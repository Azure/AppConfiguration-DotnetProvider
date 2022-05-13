// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class EndpointUriComparer : IEqualityComparer<Uri>
    {
        public bool Equals(Uri endpoint1, Uri endpoint2)
        {
            return Compare(endpoint1, endpoint2) == 0;
        }

        public int GetHashCode(Uri obj)
        {
            if (obj is Uri uri)
            {
                // Have to convert the normalizedHost to lower case to ensure case insensetive comparison. string.GetHashCode(StringComparison.OrdinalIgnoreCase) isn't available in netstandard2.0
                string componentsToHash = uri.GetComponents(UriComponents.NormalizedHost | UriComponents.Port, UriFormat.SafeUnescaped)?.ToLower();
                return componentsToHash != null ? componentsToHash.GetHashCode() : -1;
            }

            return -1;
        }

        private int Compare(Uri endpoint1, Uri endpoint2)
        {
            return Uri.Compare(endpoint1,
                               endpoint2,
                               UriComponents.NormalizedHost | UriComponents.Port,
                               UriFormat.SafeUnescaped,
                               StringComparison.OrdinalIgnoreCase);
        }
    }
}
