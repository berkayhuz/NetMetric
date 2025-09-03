// <copyright file="Result.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Common;

/// <summary>
/// Represents error codes for common operation results used throughout the NetMetric pipeline.
/// </summary>
/// <remarks>
/// <para>
/// These codes are intentionally compact and stable to allow consistent handling across boundaries
/// (collectors, exporters, and host applications). Use <see cref="Unexpected"/> only for truly unforeseen
/// failures; prefer a more specific code whenever possible.
/// </para>
/// </remarks>
public enum ErrorCode
{
    /// <summary>No error occurred.</summary>
    None = 0,

    /// <summary>The operation was cancelled.</summary>
    Cancelled = 1,

    /// <summary>The operation timed out.</summary>
    Timeout = 2,

    /// <summary>An invalid argument was provided.</summary>
    InvalidArgument = 3,

    /// <summary>The operation was in an invalid state.</summary>
    InvalidState = 4,

    /// <summary>The item already exists.</summary>
    AlreadyExists = 5,

    /// <summary>The item was not found.</summary>
    NotFound = 6,

    /// <summary>An error occurred during export.</summary>
    ExporterError = 7,

    /// <summary>An unexpected error occurred.</summary>
    Unexpected = 99
}

/// <summary>
/// Represents the outcome of an operation which is either a success (with an optional value)
/// or a failure (with an error message and an <see cref="ErrorCode"/>).
/// </summary>
/// <typeparam name="T">The type of the value carried on success.</typeparam>
/// <remarks>
/// <para>
/// This is a minimal, allocation-friendly result container suitable for returning from public APIs.
/// Prefer using the factory helpers on <see cref="Result"/> (e.g., <see cref="Result.Success{T}(T)"/>,
/// <see cref="Result.Failure{T}(string, ErrorCode)"/>) to construct instances in user code.
/// </para>
/// <para><b>Deconstruction:</b> The type supports <c>var (ok, value, code) = result;</c> for ergonomic checks.</para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code language="csharp"><![CDATA[
/// Result<int> ParsePositive(string s)
/// {
///     if (!int.TryParse(s, out var v))
///         return Result.Failure<int>($"'{s}' is not an integer.", ErrorCode.InvalidArgument);
///     if (v <= 0)
///         return Result.Failure<int>("Value must be > 0.", ErrorCode.InvalidArgument);
///     return Result.Success(v);
/// }
///
/// var r = ParsePositive("42");
/// if (r.IsSuccess)
///     Console.WriteLine(r.Value);      // 42
/// else
///     Console.WriteLine($"{r.Code}: {r.Error}");
/// ]]></code>
/// Pattern with deconstruction:
/// <code language="csharp"><![CDATA[
/// var (ok, value, code) = r;
/// if (!ok && code == ErrorCode.InvalidArgument) { /* handle */ }
/// ]]></code>
/// </example>
public sealed class Result<T>
{
    /// <summary>
    /// Gets a value indicating whether the result represents success.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the value of the result if successful; otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// For value types, prefer accessing via pattern checks on <see cref="IsSuccess"/> before using <see cref="Value"/>.
    /// </remarks>
    public T? Value { get; }

    /// <summary>
    /// Gets the error message if the result is a failure; otherwise <see langword="null"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets the error code associated with the result.
    /// </summary>
    public ErrorCode Code { get; }

    /// <summary>
    /// Initializes a new <see cref="Result{T}"/> instance.
    /// </summary>
    /// <param name="ok"><see langword="true"/> for success; <see langword="false"/> for failure.</param>
    /// <param name="value">Optional value for success cases.</param>
    /// <param name="error">Optional error message for failure cases.</param>
    /// <param name="code">Error code (use <see cref="ErrorCode.None"/> for success).</param>
    private Result(bool ok, T? value, string? error, ErrorCode code)
        => (IsSuccess, Value, Error, Code) = (ok, value, error, code);

    /// <summary>
    /// Deconstructs the result into a success flag, value, and error code.
    /// </summary>
    /// <param name="ok">Outputs whether the result indicates success.</param>
    /// <param name="value">Outputs the value when successful; otherwise default.</param>
    /// <param name="code">Outputs the associated error code.</param>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var (ok, value, code) = service.TryGetUser(id);
    /// if (ok) { /* use value */ } else if (code == ErrorCode.NotFound) { /* 404 */ }
    /// ]]></code>
    /// </example>
    public void Deconstruct(out bool ok, out T? value, out ErrorCode code)
        => (ok, value, code) = (IsSuccess, Value, Code);

    /// <summary>
    /// Creates a successful <see cref="Result{T}"/> with an optional value.
    /// </summary>
    /// <param name="value">The value to carry with the success (optional).</param>
    /// <returns>A success result.</returns>
    internal static Result<T> CreateSuccess(T? value = default)
        => new(true, value, null, ErrorCode.None);

    /// <summary>
    /// Creates a failed <see cref="Result{T}"/> with the specified message and error code.
    /// </summary>
    /// <param name="error">A non-empty human-readable error message.</param>
    /// <param name="code">The error code (default: <see cref="ErrorCode.Unexpected"/>).</param>
    /// <returns>A failure result.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="error"/> is <see langword="null"/> or empty/whitespace.
    /// </exception>
    internal static Result<T> CreateFailure(string error, ErrorCode code = ErrorCode.Unexpected)
        => string.IsNullOrWhiteSpace(error)
            ? throw new ArgumentException("Error message cannot be null or empty.", nameof(error))
            : new(false, default, error, code);
}

/// <summary>
/// Factory helpers for constructing <see cref="Result{T}"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Prefer these helpers to keep call sites concise and consistent. Both methods are allocation-minimal
/// and do not throw unless input validation fails (e.g., empty error message for <see cref="Failure{T}(string, ErrorCode)"/>).
/// </para>
/// </remarks>
public static class Result
{
    /// <summary>
    /// Creates a successful result with an optional value.
    /// </summary>
    /// <typeparam name="T">The value type carried on success.</typeparam>
    /// <param name="value">The value to return.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// return Result.Success(42);               // Result<int>
    /// return Result.Success<string>(null);     // Result<string> with no payload
    /// ]]></code>
    /// </example>
    public static Result<T> Success<T>(T? value = default)
        => Result<T>.CreateSuccess(value);

    /// <summary>
    /// Creates a failed result with the specified error and error code.
    /// </summary>
    /// <typeparam name="T">The value type that would be returned on success.</typeparam>
    /// <param name="error">Human-readable error message (required).</param>
    /// <param name="code">Categorical error code (default: <see cref="ErrorCode.Unexpected"/>).</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="error"/> is <see langword="null"/> or empty/whitespace.
    /// </exception>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// return Result.Failure<User>("User not found.", ErrorCode.NotFound);
    /// ]]></code>
    /// </example>
    public static Result<T> Failure<T>(string error, ErrorCode code = ErrorCode.Unexpected)
        => Result<T>.CreateFailure(error, code);
}
