using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>Web <c>fuzzIter</c> 的爆炸参数（debug 工具 <c>ringScale</c> / <c>impulse</c>）。</summary>
public readonly record struct CrowdWebFuzzBlast(Fix64 RingScale, Fix64 Impulse);
