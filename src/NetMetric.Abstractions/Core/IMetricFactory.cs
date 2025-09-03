// <copyright file="IMetricFactory.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Abstractions;

/// <summary>
/// Factory contract to abstract concrete metric types.
/// Collectors only depend on this interface; concrete classes remain internal to the NetMetric package.
/// </summary>
public interface IMetricFactory
{
    IGaugeBuilder Gauge(string id, string name);
    ICounterBuilder Counter(string id, string name);
    ITimerBuilder Timer(string id, string name);
    ISummaryBuilder Summary(string id, string name);
    IBucketHistogramBuilder Histogram(string id, string name);
    IMultiGaugeBuilder MultiGauge(string id, string name);
}
