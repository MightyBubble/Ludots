using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Screen-space rectangle presenter lane: every frame, each active ScreenRect behavior
    /// renders the rectangle spanned by its two param-driven corners into the per-frame
    /// screen overlay buffer. The buffer is lifted to the TopMost overlay layer by the
    /// overlay scene builder and cleared by the host after compositing, so the rect follows
    /// the data while it is written and disappears with the presenter (e.g. scope-scoped
    /// drag marquee: scope destroyed → presenter destroyed → no rect written next frame).
    /// </summary>
    public sealed class PresenterScreenRectSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription RectQuery = new QueryDescription()
            .WithAll<PresenterState, PerfHasScreenRect>()
            .WithNone<PresenterBootstrapPending>();

        private readonly PresenterDefinitionRegistry _definitions;
        private readonly ScreenOverlayBuffer _overlay;
        private int _cachedDefinitionVersion = -1;
        private PresenterDefinition.ScreenRectWorkItem[][] _rectWorkByDefinition =
            Array.Empty<PresenterDefinition.ScreenRectWorkItem[]>();

        public PresenterScreenRectSystem(
            World world,
            PresenterDefinitionRegistry definitions,
            ScreenOverlayBuffer overlay)
            : base(world)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        }

        public override void Update(in float dt)
        {
            EnsureDefinitionWorkCache();

            foreach (ref var chunk in World.Query(in RectQuery))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                bool hasFloatParams = chunk.Has<PresenterFloatParams>();
                bool hasFloatDefaults = chunk.Has<PresenterFloatDefaults>();
                bool hasVectorParams = chunk.Has<PresenterVectorParams>();
                bool hasVectorDefaults = chunk.Has<PresenterVectorDefaults>();
                bool hasIntParams = chunk.Has<PresenterIntParams>();
                bool hasIntDefaults = chunk.Has<PresenterIntDefaults>();
                Span<PresenterFloatParams> floatParams = hasFloatParams ? chunk.GetSpan<PresenterFloatParams>() : default;
                Span<PresenterFloatDefaults> floatDefaults = hasFloatDefaults ? chunk.GetSpan<PresenterFloatDefaults>() : default;
                Span<PresenterVectorParams> vectorParams = hasVectorParams ? chunk.GetSpan<PresenterVectorParams>() : default;
                Span<PresenterVectorDefaults> vectorDefaults = hasVectorDefaults ? chunk.GetSpan<PresenterVectorDefaults>() : default;
                Span<PresenterIntParams> intParams = hasIntParams ? chunk.GetSpan<PresenterIntParams>() : default;
                Span<PresenterIntDefaults> intDefaults = hasIntDefaults ? chunk.GetSpan<PresenterIntDefaults>() : default;

                for (int i = 0; i < states.Length; i++)
                {
                    ref readonly PresenterState state = ref states[i];
                    PresenterDefinition.ScreenRectWorkItem[] workItems =
                        (uint)state.DefId < (uint)_rectWorkByDefinition.Length
                            ? _rectWorkByDefinition[state.DefId]
                            : null;
                    if (workItems == null)
                    {
                        continue;
                    }
                    for (int w = 0; w < workItems.Length; w++)
                    {
                        ref readonly PresenterDefinition.ScreenRectWorkItem work = ref workItems[w];
                        if (!IsBehaviorActive(state.BehaviorActiveMask, work.SlotIndex) ||
                            !IsVisible(work.Rect.VisibilityParamKey, hasIntParams, hasIntDefaults, hasFloatParams, hasFloatDefaults, in intParams, in intDefaults, in floatParams, in floatDefaults, i) ||
                            !TryResolveCorner(work.Rect.Corner0XParamKey, hasFloatParams, hasFloatDefaults, in floatParams, in floatDefaults, i, out float corner0X) ||
                            !TryResolveCorner(work.Rect.Corner0YParamKey, hasFloatParams, hasFloatDefaults, in floatParams, in floatDefaults, i, out float corner0Y) ||
                            !TryResolveCorner(work.Rect.Corner1XParamKey, hasFloatParams, hasFloatDefaults, in floatParams, in floatDefaults, i, out float corner1X) ||
                            !TryResolveCorner(work.Rect.Corner1YParamKey, hasFloatParams, hasFloatDefaults, in floatParams, in floatDefaults, i, out float corner1Y))
                        {
                            continue;
                        }

                        float minX = MathF.Min(corner0X, corner1X);
                        float minY = MathF.Min(corner0Y, corner1Y);
                        float maxX = MathF.Max(corner0X, corner1X);
                        float maxY = MathF.Max(corner0Y, corner1Y);
                        float width = maxX - minX;
                        float height = maxY - minY;
                        if (width < 1f || height < 1f)
                        {
                            continue;
                        }

                        Vector4 fill = ResolveColor(work.Rect.FillColor, work.Rect.FillColorParamKey, hasVectorParams, hasVectorDefaults, in vectorParams, in vectorDefaults, i);
                        Vector4 border = ResolveColor(work.Rect.BorderColor, work.Rect.BorderColorParamKey, hasVectorParams, hasVectorDefaults, in vectorParams, in vectorDefaults, i);
                        _overlay.AddRect(
                            (int)minX,
                            (int)minY,
                            (int)width,
                            (int)height,
                            fill,
                            border,
                            PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, work.SlotIndex),
                            dirtySerial: 0);
                    }
                }
            }
        }

        private void EnsureDefinitionWorkCache()
        {
            if (_cachedDefinitionVersion == _definitions.Version)
            {
                return;
            }

            System.Collections.Generic.IReadOnlyList<int> registeredIds = _definitions.RegisteredIds;
            int maxId = 0;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                maxId = Math.Max(maxId, registeredIds[i]);
            }

            if (_rectWorkByDefinition.Length <= maxId)
            {
                _rectWorkByDefinition = new PresenterDefinition.ScreenRectWorkItem[maxId + 1][];
            }
            else
            {
                Array.Clear(_rectWorkByDefinition);
            }

            for (int i = 0; i < registeredIds.Count; i++)
            {
                int definitionId = registeredIds[i];
                if (_definitions.TryGet(definitionId, out PresenterDefinition definition) &&
                    definition.HasScreenRectBehavior &&
                    definition.ScreenRectWorkItems.Length != 0)
                {
                    _rectWorkByDefinition[definitionId] = definition.ScreenRectWorkItems;
                }
            }

            _cachedDefinitionVersion = _definitions.Version;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static bool IsVisible(
            int visibilityParamKey,
            bool hasIntParams,
            bool hasIntDefaults,
            bool hasFloatParams,
            bool hasFloatDefaults,
            in Span<PresenterIntParams> intParams,
            in Span<PresenterIntDefaults> intDefaults,
            in Span<PresenterFloatParams> floatParams,
            in Span<PresenterFloatDefaults> floatDefaults,
            int index)
        {
            if (visibilityParamKey < 0)
            {
                return true;
            }

            if (hasIntParams && intParams[index].TryGet(visibilityParamKey, out int intValue))
            {
                return intValue != 0;
            }

            if (hasIntDefaults && intDefaults[index].TryGet(visibilityParamKey, out int intDefaultValue))
            {
                return intDefaultValue != 0;
            }

            if (hasFloatParams && floatParams[index].TryGet(visibilityParamKey, out float floatValue))
            {
                return floatValue > 0.5f;
            }

            if (hasFloatDefaults && floatDefaults[index].TryGet(visibilityParamKey, out float floatDefaultValue))
            {
                return floatDefaultValue > 0.5f;
            }

            return false;
        }

        private static bool TryResolveCorner(
            int paramKey,
            bool hasFloatParams,
            bool hasFloatDefaults,
            in Span<PresenterFloatParams> floatParams,
            in Span<PresenterFloatDefaults> floatDefaults,
            int index,
            out float value)
        {
            if (paramKey >= 0 && hasFloatParams && floatParams[index].TryGet(paramKey, out float paramValue) && float.IsFinite(paramValue))
            {
                value = paramValue;
                return true;
            }

            if (paramKey >= 0 && hasFloatDefaults && floatDefaults[index].TryGet(paramKey, out float defaultValue) && float.IsFinite(defaultValue))
            {
                value = defaultValue;
                return true;
            }

            value = 0f;
            return false;
        }

        private static Vector4 ResolveColor(
            Vector4 authoredColor,
            int colorParamKey,
            bool hasVectorParams,
            bool hasVectorDefaults,
            in Span<PresenterVectorParams> vectorParams,
            in Span<PresenterVectorDefaults> vectorDefaults,
            int index)
        {
            if (colorParamKey >= 0 && hasVectorParams && vectorParams[index].TryGet(colorParamKey, out Vector4 color))
            {
                return color;
            }

            if (colorParamKey >= 0 && hasVectorDefaults && vectorDefaults[index].TryGet(colorParamKey, out Vector4 defaultColor))
            {
                return defaultColor;
            }

            return authoredColor;
        }
    }
}
