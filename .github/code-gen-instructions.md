# AppConfiguration-DotnetProvider Coding Guidelines

This document outlines coding guidelines for the Azure App Configuration .NET Provider repository. Follow these guidelines when generating or modifying code.

## General Guidelines

1. **Exception Handling**:
   * When adding error handling, always catch specific exceptions and avoid catching the base `Exception` class in catch blocks.
   * Throw specific exception types (e.g., `ArgumentNullException`, `FormatException`, custom exceptions) rather than generic `System.Exception`.
   * Include the parameter name when throwing `ArgumentNullException` using `nameof()`.

2. **Variable Declaration**:
   * Never use `var` to declare a variable if the assignment doesn't include the type or the type isn't immediately obvious.
   * Use explicit type names for fields, properties, method parameters, and return types.
   * Use `var` only when the type is obvious from the right-hand side (e.g., `var user = new User();`).

3. **Null Handling**:
   * Validate arguments in public methods and constructors with explicit null checks.
   * Use explicit `if (argument == null) throw new ArgumentNullException(nameof(argument));` checks at the beginning of methods/constructors.
   * Avoid using the null-forgiving operator (`!`) unless absolutely necessary.

4. **Asynchronous Programming**:
   * All async methods should accept a `CancellationToken` as the last parameter.
   * Pass the `cancellationToken` down the call stack to all subsequent asynchronous operations.
   * Use `Task<T>` or `Task` for asynchronous methods.

5. **LINQ and Collections**:
   * Prefer simple, readable LINQ queries.
   * Break down complex LINQ queries into separate statements with intermediate variables.
   * Use collection interfaces (e.g., `IList<T>`, `IReadOnlyList<T>`) in parameter and return types.

6. **Resource Management**:
   * Wrap `IDisposable` instances in `using` statements to ensure proper disposal.
   * Implement `IDisposable` correctly if your class manages disposable objects.

7. **Dependency Injection**:
   * Use constructor injection for dependencies.
   * Store injected dependencies in `private readonly` fields.
   * Validate injected dependencies for null in the constructor.

8. **Naming Conventions**:
   * Use `PascalCase` for classes, interfaces, enums, methods, properties, and constants.
   * Use `camelCase` for local variables and method parameters.
   * Prefix private fields with an underscore (`_`).
   * Define constants for error messages and other string literals.

9. **Comments**:
    * Only add comments when it's not obvious what the code is doing. For example, if a variable name is already fairly descriptive, a comment isn't needed explaining its name.
    * Add summary comments to public classes and members of those classes.

## AppConfiguration-Specific Guidelines

1. **Feature Flag Handling**:
   * Validate feature flag data structure before processing.
   * Handle different feature flag schemas (Microsoft vs .NET) appropriately.
   * Use proper error handling when parsing feature flags with clear error messages.

2. **Configuration Key-Value Processing**:
   * Follow adapter pattern for processing different configuration types.
   * Properly handle key-value pairs with appropriate content type detection.
   * Use `KeyValuePair<string, string>` for configuration values.

3. **Content Type Handling**:
   * Validate content types before processing.
   * Use appropriate content type constants.
   * Check content type using extension methods like `IsFeatureFlag()`.

4. **JSON Parsing**:
   * Use `Utf8JsonReader` for performance-critical JSON parsing.
   * Validate JSON structure and provide clear error messages for malformed input.
   * Handle JSON token types appropriately with proper error handling.

5. **Refresh Mechanisms**:
   * Implement proper configuration refresh patterns.
   * Use sentinel-based refresh mechanisms when appropriate.
   * Handle refresh failures gracefully.

## Performance Considerations

1. **String Handling**:
   * Use `StringBuilder` for concatenating multiple strings.
   * Define string constants for recurring strings.
   * Use string interpolation instead of string concatenation when appropriate.

2. **Collections**:
   * Initialize collections with estimated capacity when possible.
   * Use appropriate collection types for the use case (e.g., `List<T>`, `Dictionary<TKey, TValue>`).
   * Avoid unnecessary collection allocations.

3. **Memory Management**:
   * Use `Span<T>` and `ReadOnlySpan<T>` for high-performance scenarios.
   * Minimize allocations in performance-critical paths.
   * Be mindful of closure allocations in LINQ and lambdas.