// <copyright file="MacSysctl.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Platform;

/// <summary>
/// macOS sysctl wrappers for CPU/OS metadata reads without external libraries.
/// Provides a clean API (Try* methods) that is robust against EINTR/ENOMEM errors,
/// and is .NET 9-friendly (uses <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/>).
/// </summary>
internal static partial class MacSysctl
{
    static MacSysctl()
    {
        // Instead of throwing, set a flag or log the unsupported platform
        if (!OperatingSystem.IsMacOS())
        {
            // Optionally log the unsupported platform
            // Log.Warning("MacSysctl is only supported on macOS.");
        }
    }

    // POSIX errno (Darwin)
    private const int EINTR = 4;   // Interrupted system call
    private const int ENOMEM = 12;  // Buffer too small / out of memory

    /// <summary>
    /// P/Invoke to the sysctlbyname function in libSystem.dylib.
    /// </summary>
    [LibraryImport("/usr/lib/libSystem.dylib",
        EntryPoint = "sysctlbyname",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sysctlbyname(string name, nint oldp, ref nuint oldlenp, nint newp, nuint newlen);

    /// <summary>
    /// Reads a NUL-terminated UTF-8 sysctl string value.
    /// </summary>
    /// <param name="key">The sysctl key to read.</param>
    /// <param name="value">The resulting value as a string.</param>
    /// <returns>Returns true if the read operation is successful; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryReadString(string key, out string? value)
    {
        value = null;

        if (!TryProbeSize(key, out var size) || size == 0)
            return false;

        var buf = Marshal.AllocHGlobal((nint)size);
        try
        {
            if (!TryReadWithRetriesAndRealloc(key, ref buf, ref size))
                return false;

            var len = checked((int)size);
            if (len <= 0)
                return false;

            // Exclude trailing NUL; trim CR/LF if present.
            var s = Marshal.PtrToStringUTF8(buf, len - 1)?.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(s))
                return false;

            value = s;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// Reads a sysctl value as an array of UInt64.
    /// </summary>
    /// <param name="key">The sysctl key to read.</param>
    /// <param name="values">The resulting values as an array of UInt64.</param>
    /// <returns>Returns true if the read operation is successful; otherwise, false.</returns>
    public static bool TryReadUInt64Array(string key, out ulong[]? values)
    {
        values = null;

        if (!TryProbeSize(key, out var size) || size == 0)
            return false;

        var buf = Marshal.AllocHGlobal((nint)size);
        try
        {
            if (!TryReadWithRetriesAndRealloc(key, ref buf, ref size) || size == 0)
                return false;

            if (size % (nuint)sizeof(ulong) != 0)
                return false;

            var bytes = new byte[checked((int)size)];
            Marshal.Copy(buf, bytes, 0, bytes.Length);

            var ulongs = MemoryMarshal.Cast<byte, ulong>(bytes);
            if (ulongs.Length == 0)
                return false;

            values = ulongs.ToArray();
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ---------- Internals ----------

    /// <summary>
    /// Probes the required buffer size for a sysctl key (oldp = NULL).
    /// Retries on EINTR/ENOMEM.
    /// </summary>
    /// <param name="key">The sysctl key to probe.</param>
    /// <param name="size">The size of the buffer needed to store the sysctl value.</param>
    /// <returns>Returns true if the probe is successful; otherwise, false.</returns>
    private static bool TryProbeSize(string key, out nuint size)
    {
        size = 0;
        const int maxAttempts = 2;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var rc = sysctlbyname(key, 0, ref size, 0, 0);
            if (rc == 0 && size > 0)
                return true;

            var errno = Marshal.GetLastPInvokeError();
            if (errno == EINTR || errno == ENOMEM)
                continue;

            return false;
        }

        return size > 0;
    }

    /// <summary>
    /// Reads the sysctl value into the provided (possibly resized) buffer.
    /// Retries on EINTR and grows the buffer on ENOMEM (kernel updates 'size').
    /// </summary>
    /// <param name="key">The sysctl key to read.</param>
    /// <param name="buf">The unmanaged buffer to store the value.</param>
    /// <param name="size">The size of the buffer.</param>
    /// <returns>Returns true if the read operation is successful; otherwise, false.</returns>
    private static bool TryReadWithRetriesAndRealloc(string key, ref nint buf, ref nuint size)
    {
        const int maxAttempts = 3;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var rc = sysctlbyname(key, buf, ref size, 0, 0);
            if (rc == 0)
                return size > 0;

            var errno = Marshal.GetLastPInvokeError();

            if (errno == EINTR)
            {
                // Interrupted by a signal; try again with same buffer.
                continue;
            }

            if (errno == ENOMEM)
            {
                // Kernel set 'size' to the required length. Reallocate and retry.
                if (size == 0)
                    return false;

                buf = Realloc(buf, (nint)size);
                continue;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Reallocate unmanaged memory to the given size, freeing the old block.
    /// We don't need to preserve previous contents for sysctl retries.
    /// </summary>
    /// <param name="oldPtr">The pointer to the old memory block.</param>
    /// <param name="newSize">The new size of the memory block.</param>
    /// <returns>The pointer to the newly allocated memory block.</returns>
    private static nint Realloc(nint oldPtr, nint newSize)
    {
        Marshal.FreeHGlobal(oldPtr);
        return Marshal.AllocHGlobal(newSize);
    }
}
