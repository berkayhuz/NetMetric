// <copyright file="PlatformDefaultProcessIoReader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.SystemIO.Readers;

internal sealed class PlatformDefaultProcessIoReader : IProcessIoReader
{
    private readonly IProcessIoReader _impl;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformDefaultProcessIoReader"/> class.
    /// </summary>
    /// <param name="impl">The platform-specific implementation of <see cref="IProcessIoReader"/>. 
    /// The appropriate implementation is injected via dependency injection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="impl"/> is null.</exception>
    public PlatformDefaultProcessIoReader(IProcessIoReader impl)
    {
        _impl = impl ?? throw new ArgumentNullException(nameof(impl));
    }

    /// <summary>
    /// Attempts to read the current process I/O snapshot.
    /// </summary>
    /// <returns>An <see cref="IoSnapshot"/> containing the current process I/O data, or null if data is unavailable.</returns>
    public IoSnapshot? TryReadCurrent() => _impl.TryReadCurrent();
}
