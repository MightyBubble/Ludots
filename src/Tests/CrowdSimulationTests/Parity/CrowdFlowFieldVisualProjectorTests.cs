using System;
using System.Linq;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace CrowdSimulationTests.Parity;

/// <summary>
/// 流场投影器契约:到达格写成 Vector4(方向, 强度)单元,未到达 / 无路点 / 原地格不写;
/// 描述符走 Flow 类 + Vector4 值型(渲染端契约的另一半)。
/// </summary>
[TestFixture]
public class CrowdFlowFieldVisualProjectorTests
{
    [Test]
    public void Project_WritesDirectionAndStrengthPerReachedCell()
    {
        const int n = 8;
        int n2 = n * n;
        var integ = new Fix64[2 * n2];
        var len = new Fix64[2 * n2];
        var wp = new int[2 * n2];
        Array.Fill(integ, Fix64.MaxValue);
        Array.Fill(len, Fix64.MaxValue);
        Array.Fill(wp, -1);
        int goal = 3 * n + 6;
        integ[goal] = Fix64.Zero;
        len[goal] = Fix64.Zero;
        wp[goal] = goal;
        // (4,3) → 路点 (6,3):方向 +X;len 8 → 强度 max(0.35, 1/(1+0.32))
        int u = 3 * n + 4;
        integ[u] = Fix64.FromInt(2);
        len[u] = Fix64.FromInt(8);
        wp[u] = goal;
        var flow = new FlowField { Integ = integ, Len = len, Wp = wp, Goal = goal, Reached = 2 };

        var source = new CrowdFlowFieldVisualSource { Flow = flow, CellCount = n, CellSizeCm = 100 };
        var projector = new CrowdFlowFieldVisualProjector(source);
        var buffer = new GlobalFieldVisualBuffer(2, 16, 2);
        buffer.BeginFrame();
        projector.Project(buffer);

        var record = buffer.GetRecords().ToArray().Single(r => r.IsActive);
        Assert.That(record.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Flow));
        Assert.That(record.Descriptor.ValueKind, Is.EqualTo(GlobalFieldVisualValueKind.Vector4));
        Assert.That(record.Descriptor.CellSizeCm, Is.EqualTo(100));
        var cells = buffer.GetCells(record).ToArray();
        Assert.That(cells.Length, Is.EqualTo(1));
        Assert.That((cells[0].Cell.X, cells[0].Cell.Y), Is.EqualTo((4, 3)));
        Assert.That(cells[0].FloatValue.X, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(cells[0].FloatValue.Y, Is.EqualTo(0f).Within(1e-6f));
        Assert.That(cells[0].FloatValue.Z, Is.EqualTo(1f / 1.32f).Within(1e-3f));
    }

    [Test]
    public void Project_NullFlow_WritesNothing()
    {
        var source = new CrowdFlowFieldVisualSource { Flow = null, CellCount = 8, CellSizeCm = 100 };
        var projector = new CrowdFlowFieldVisualProjector(source);
        var buffer = new GlobalFieldVisualBuffer(2, 16, 2);
        buffer.BeginFrame();
        projector.Project(buffer);
        Assert.That(buffer.GetRecords().ToArray().Count(r => r.IsActive), Is.Zero);
    }
}
