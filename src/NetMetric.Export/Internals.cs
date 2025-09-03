// <copyright file="Internals.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NetMetric.Export.AzureMonitor")]
[assembly: InternalsVisibleTo("NetMetric.Export.CloudWatch")]
[assembly: InternalsVisibleTo("NetMetric.Export.Elastic")]
[assembly: InternalsVisibleTo("NetMetric.Export.EventCounters")]
[assembly: InternalsVisibleTo("NetMetric.Export.InfluxDB")]
[assembly: InternalsVisibleTo("NetMetric.Export.OpenTelemetry")]
[assembly: InternalsVisibleTo("NetMetric.Export.OpenTelemetryBridge")]
[assembly: InternalsVisibleTo("NetMetric.Export.JsonConsole")]
[assembly: InternalsVisibleTo("NetMetric.Export.Prometheus")]
[assembly: InternalsVisibleTo("NetMetric.Export.Stackdriver")]
[assembly: InternalsVisibleTo("NetMetric.Export.DependencyInjection")]
