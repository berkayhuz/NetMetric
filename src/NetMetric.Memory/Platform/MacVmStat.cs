// <copyright file="MacVmStat.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Memory.Platform;

/// <summary>
/// Provides methods to interact with macOS virtual memory statistics using system APIs.
/// </summary>
internal static partial class MacVmStat
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    /// <summary>
    /// Calls the sysctlbyname function from the system library to retrieve system information.
    /// </summary>
    /// <param name="name">The name of the sysctl parameter to query.</param>
    /// <param name="oldp">A pointer to the old value.</param>
    /// <param name="oldlenp">The length of the old value.</param>
    /// <param name="newp">A pointer to the new value.</param>
    /// <param name="newlen">The length of the new value.</param>
    /// <returns>Returns 0 on success, non-zero on failure.</returns>
    [LibraryImport(LibSystem, EntryPoint = "sysctlbyname", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sysctlbyname(string name, nint oldp, ref nuint oldlenp, nint newp, nuint newlen);

    /// <summary>
    /// Calls the host_statistics64 function from the system library to retrieve virtual memory statistics.
    /// </summary>
    /// <param name="host_priv">The host privilege identifier.</param>
    /// <param name="flavor">The flavor of statistics to retrieve.</param>
    /// <param name="vmStats">The structure to hold the virtual memory statistics.</param>
    /// <param name="count">The number of elements in the structure.</param>
    /// <returns>Returns 0 on success, non-zero on failure.</returns>
    [LibraryImport(LibSystem, EntryPoint = "host_statistics64")]
    private static partial int host_statistics64(nint host_priv, int flavor, out vm_statistics64 vmStats, ref uint count);

    /// <summary>
    /// Calls the mach_host_self function from the system library to get the current host.
    /// </summary>
    /// <returns>The host identifier.</returns>
    [LibraryImport(LibSystem, EntryPoint = "mach_host_self")]
    private static partial nint mach_host_self();

    internal const int HOST_VM_INFO64 = 4;

    /// <summary>
    /// Represents the structure of virtual memory statistics on macOS.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct vm_statistics64 : IEquatable<vm_statistics64>
    {
        public ulong free_count;
        public ulong active_count;
        public ulong inactive_count;
        public ulong wire_count;

        // IEquatable<T>
        public bool Equals(vm_statistics64 other) =>
            free_count == other.free_count &&
            active_count == other.active_count &&
            inactive_count == other.inactive_count &&
            wire_count == other.wire_count;

        // object.Equals
        public override bool Equals(object? obj) =>
            obj is vm_statistics64 other && Equals(other);

        // object.GetHashCode
        public override int GetHashCode()
        {
            var hc = new HashCode();
            hc.Add(free_count);
            hc.Add(active_count);
            hc.Add(inactive_count);
            hc.Add(wire_count);
            return hc.ToHashCode();
        }

        // == ve != operatörleri
        public static bool operator ==(vm_statistics64 left, vm_statistics64 right) => left.Equals(right);
        public static bool operator !=(vm_statistics64 left, vm_statistics64 right) => !left.Equals(right);
    }

    /// <summary>
    /// Attempts to retrieve the page size used by the system.
    /// </summary>
    /// <param name="pageSize">The page size in bytes.</param>
    /// <returns>True if the page size was successfully retrieved; otherwise, false.</returns>
    internal static bool TryPageSize(out ulong pageSize)
    {
        pageSize = 0;

        nuint size = (nuint)Marshal.SizeOf<ulong>();

        var rc = sysctlbyname("hw.pagesize", 0, ref size, 0, 0);

        if (rc != 0 || size != (nuint)Marshal.SizeOf<ulong>())
        {
            return false;
        }

        var buf = Marshal.AllocHGlobal((nint)size);

        try
        {
            rc = sysctlbyname("hw.pagesize", buf, ref size, 0, 0);

            if (rc != 0)
            {
                return false;
            }

            long signed = Marshal.ReadInt64(buf);

            pageSize = unchecked((ulong)signed);

            return pageSize > 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// Attempts to retrieve the total physical memory size of the system.
    /// </summary>
    /// <param name="mem">The total physical memory in bytes.</param>
    /// <returns>True if the memory size was successfully retrieved; otherwise, false.</returns>
    internal static bool TryHwMemSize(out ulong mem)
    {
        mem = 0;

        nuint size = (nuint)Marshal.SizeOf<ulong>();

        var rc = sysctlbyname("hw.memsize", 0, ref size, 0, 0);

        if (rc != 0 || size != (nuint)Marshal.SizeOf<ulong>())
        {
            return false;
        }

        var buf = Marshal.AllocHGlobal((nint)size);

        try
        {
            rc = sysctlbyname("hw.memsize", buf, ref size, 0, 0);

            if (rc != 0)
            {
                return false;
            }

            long signed = Marshal.ReadInt64(buf);

            mem = unchecked((ulong)signed);

            return mem > 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// Attempts to retrieve virtual memory statistics from the system.
    /// </summary>
    /// <param name="vm">The structure to hold the virtual memory statistics.</param>
    /// <returns>True if the statistics were successfully retrieved; otherwise, false.</returns>
    internal static bool TryVmStats(out vm_statistics64 vm)
    {
        vm = default;

        uint count = (uint)Marshal.SizeOf<vm_statistics64>() / sizeof(uint);

        var host = mach_host_self();

        if (host == 0)
        {
            return false;
        }

        var kr = host_statistics64(host, HOST_VM_INFO64, out vm, ref count);

        return kr == 0;
    }
}