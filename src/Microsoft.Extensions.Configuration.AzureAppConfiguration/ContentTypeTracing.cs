// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Tracing for tracking certain content types.
    /// </summary>
    internal class ContentTypeTracing
    {
        /// <summary>
        /// Flag to indicate whether any key-value uses a content type with the format application/json;profile="https://azconfig.io/mime-profiles/ai".
        /// </summary>
        public bool HasAIContentTypeProfile { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether any key-value uses a content type that contains application/json;profile="https://azconfig.io/mime-profiles/ai/chat-completion".
        /// </summary>
        public bool HasAIChatCompletionContentTypeProfile { get; set; } = false;
    }
}
