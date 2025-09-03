// <copyright file="MacProcessIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NetMetric.SystemIO.Abstractions;

namespace NetMetric.SystemIO.MacOS;

/// <summary>
/// Provides methods to read process-level disk I/O statistics on macOS using <c>proc_pid_rusage</c>
/// to retrieve the bytes read and written.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacProcessIoReader : IProcessIoReader
{
    /// <summary>
    /// Attempts to read the current process disk I/O statistics, including bytes read and written.
    /// </summary>
    /// <returns>An <see cref="IoSnapshot"/> containing bytes read/written, or <c>null</c> if unavailable.</returns>
    public IoSnapshot? TryReadCurrent()
    {
        try
        {
            var info = new RusageInfoV4();
            var pid = GetPid();
            var res = ProcPidRusage(pid, RusageInfoV4Flavor, ref info);

            if (res != 0)
            {
                // Non-zero means failure per proc_pid_rusage contract.
                return null;
            }

            return new IoSnapshot(info.ri_diskio_bytesread, info.ri_diskio_byteswritten, DateTime.UtcNow);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        catch (SEHException) { return null; }
        catch                                  // unexpected -> bubble up
        {
            throw;
        }
    }

    // Use a distinct, readable name to avoid case-only differences with the struct.
    private const int RusageInfoV4Flavor = 4;

    // Bind to C symbols with EntryPoint, keep C# names .NET-friendly.
    [DllImport("libSystem.B.dylib", ExactSpelling = true, EntryPoint = "getpid")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int GetPid();

    [DllImport("libproc.dylib", ExactSpelling = true, EntryPoint = "proc_pid_rusage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int ProcPidRusage(int pid, int flavor, ref RusageInfoV4 buffer);

    /// <summary>
    /// Managed projection of <c>rusage_info_v4</c> used by <c>proc_pid_rusage</c>.
    /// Only a subset is consumed here (disk I/O fields), but the layout must match native.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RusageInfoV4 : IEquatable<RusageInfoV4>
    {
        // --- Fields used for disk I/O (and required preceding layout) ---
        public ulong ri_uuid0; public ulong ri_uuid1; public ulong ri_uuid2; public ulong ri_uuid3;
        public ulong ri_user_time; public ulong ri_system_time; public ulong ri_pkg_idle_wkups;
        public ulong ri_interrupt_wkups; public ulong ri_pageins; public ulong ri_wired_size;
        public ulong ri_resident_size; public ulong ri_phys_footprint; public ulong ri_proc_start_abstime;
        public ulong ri_proc_exit_abstime; public ulong ri_child_user_time; public ulong ri_child_system_time;
        public ulong ri_child_pkg_idle_wkups; public ulong ri_child_interrupt_wkups; public ulong ri_child_pageins;
        public ulong ri_child_elapsed_abstime; public ulong ri_diskio_bytesread; public ulong ri_diskio_byteswritten;

        // Other RUSAGE_INFO_V4 fields (kept for correct size/layout).
        public ulong ri_cpu_time_qos_default, ri_cpu_time_qos_maintenance, ri_cpu_time_qos_background,
                     ri_cpu_time_qos_utility, ri_cpu_time_qos_legacy, ri_cpu_time_qos_user_initiated,
                     ri_cpu_time_qos_user_interactive, ri_billed_system_time, ri_serviced_system_time, ri_logical_writes,
                     ri_lifetime_max_phys_footprint, ri_instructions, ri_cycles, ri_billed_energy, ri_serviced_energy,
                     ri_interval_max_phys_footprint, ri_runnable_time, ri_idle_wkups, ri_power_state_total_time,
                     ri_submit_bandwidth, ri_total_syscalls, ri_vmach_faults, ri_platform_idle_wkups, ri_coalitions_wakes,
                     ri_platform_idle_bg_wkups, ri_latency_qos_tier0_time, ri_latency_qos_tier1_time, ri_latency_qos_tier2_time,
                     ri_latency_qos_tier3_time, ri_latency_qos_tier4_time, ri_latency_qos_tier5_time, ri_latency_qos_tier6_time,
                     ri_wired_size_bytes_avg, ri_resident_size_bytes_avg, ri_phys_footprint_bytes_avg, ri_user_time_mach,
                     ri_system_time_mach, ri_billed_energy_nj, ri_serviced_energy_nj;

        // ---- Equality members (value semantics) ----
        public bool Equals(RusageInfoV4 other) =>
            ri_diskio_bytesread == other.ri_diskio_bytesread &&
            ri_diskio_byteswritten == other.ri_diskio_byteswritten &&
            ri_user_time == other.ri_user_time &&
            ri_system_time == other.ri_system_time &&
            ri_resident_size == other.ri_resident_size &&
            ri_phys_footprint == other.ri_phys_footprint;

        public override bool Equals(object? obj) => obj is RusageInfoV4 o && Equals(o);

        public override int GetHashCode() =>
            HashCode.Combine(
                ri_diskio_bytesread, ri_diskio_byteswritten,
                ri_user_time, ri_system_time, ri_resident_size, ri_phys_footprint);

        public static bool operator ==(RusageInfoV4 left, RusageInfoV4 right) => left.Equals(right);
        public static bool operator !=(RusageInfoV4 left, RusageInfoV4 right) => !left.Equals(right);
    }
}
