using System;
using System.Numerics;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 可走区域层 → 全场视觉缓冲的投影器(S2 视图的场叠加版):
/// 不可走 = 红罩,桥面 = 棕,桥头 portal = 黄;配色即 S2 校验图的语义。
/// 值是 Vector4 直接 RGBA(渲染端不再映射,见 Walkable 场契约)。
/// </summary>
public sealed class CrowdWalkableVisualProjector : IGlobalFieldVisualProjector
{
    private static readonly Vector4 BlockedRed = new(0.63f, 0.12f, 0.12f, 0.5f);
    private static readonly Vector4 DeckBrown = new(0.59f, 0.43f, 0.27f, 0.85f);
    private static readonly Vector4 PortalYellow = new(1f, 0.84f, 0.31f, 0.9f);

    private readonly CrowdFlowFieldVisualSource _source;
    private readonly Func<NavContext?> _navSource;
    private GlobalFieldVisualCell[] _cells = new GlobalFieldVisualCell[8192];
    private IntRect[] _dirty = new IntRect[1];

    public CrowdWalkableVisualProjector(CrowdFlowFieldVisualSource source, Func<NavContext?> navSource)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _navSource = navSource ?? throw new ArgumentNullException(nameof(navSource));
    }

    public void Project(GlobalFieldVisualBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var nav = _navSource();
        int n = _source.CellCount;
        if (nav == null || n <= 0) return;

        int n2 = n * n;
        if (_cells.Length < n2) _cells = new GlobalFieldVisualCell[n2];
        int count = 0;
        for (int c = 0; c < n2; c++)
        {
            Vector4 color;
            if (nav.Portal[c] != 0 && nav.UpPass[c] != 0) color = PortalYellow;
            else if (nav.UpPass[c] != 0) color = DeckBrown;
            else if (nav.Passable[c] == 0) color = BlockedRed;
            else continue;
            _cells[count++] = new GlobalFieldVisualCell(new Ludots.Core.Fields.FieldCell2D(c % n, c / n), color);
        }

        if (count == 0) return;
        _dirty[0] = new IntRect(0, 0, n, n);
        var descriptor = new GlobalFieldVisualDescriptor(
            new GlobalFieldVisualId(GlobalFieldVisualKind.Walkable, scopeKeyId: 1, layerKeyId: 0, surfaceKeyId: 0),
            _source.CellSizeCm,
            new Platform.Abstractions.WorldCmInt2(0, 0),
            new IntRect(0, 0, n, n),
            GlobalFieldVisualValueKind.Vector4);
        buffer.Upsert(in descriptor, _cells.AsSpan(0, count), _dirty);
    }
}
