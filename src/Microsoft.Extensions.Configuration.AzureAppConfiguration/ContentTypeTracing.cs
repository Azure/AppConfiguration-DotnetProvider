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
        /// Flag to indicate whether any key-value uses the json content type and contains
        /// a parameter indicating an AI profile.
        /// </summary>
        public bool HasAIProfile { get; set; } = false;

        /// <summary>
        /// Flag to indicate whether any key-value uses the json content type and contains
        /// a parameter indicating an AI chat completion profile.
        /// </summary>
        public bool HasAIChatCompletionProfile { get; set; } = false;
    }
}
