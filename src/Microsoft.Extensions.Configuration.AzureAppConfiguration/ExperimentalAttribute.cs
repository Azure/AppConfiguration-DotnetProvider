// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

#if !NET8_0_OR_GREATER

#nullable enable

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    ///  Indicates that an API is experimental and it may change in the future.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly |
                    AttributeTargets.Module |
                    AttributeTargets.Class |
                    AttributeTargets.Struct |
                    AttributeTargets.Enum |
                    AttributeTargets.Constructor |
                    AttributeTargets.Method |
                    AttributeTargets.Property |
                    AttributeTargets.Field |
                    AttributeTargets.Event |
                    AttributeTargets.Interface |
                    AttributeTargets.Delegate, Inherited = false)]
    internal sealed class ExperimentalAttribute : Attribute
    {
        /// <summary>
        ///  Initializes a new instance of the <see cref="ExperimentalAttribute"/> class.
        /// </summary>
        /// <param name="diagnosticId">The ID that the compiler will use when reporting a use of the API the attribute applies to.</param>
        public ExperimentalAttribute(string diagnosticId)
        {
            DiagnosticId = diagnosticId;
        }

        /// <summary>
        ///  Gets the ID that the compiler will use when reporting a use of the API the attribute applies to.
        /// </summary>
        public string DiagnosticId { get; }

        /// <summary>
        ///  Gets or sets the URL for corresponding documentation.
        /// </summary>
        public string? UrlFormat { get; set; }
    }
}
#endif
