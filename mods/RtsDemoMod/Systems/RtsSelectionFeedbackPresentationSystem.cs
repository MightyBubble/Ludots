using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Systems
{
    public sealed class RtsSelectionFeedbackPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly GroundOverlayBuffer _overlays;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly AbilityDefinitionRegistry? _abilityDefinitions;
        private float _elapsedSeconds;

        public RtsSelectionFeedbackPresentationSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer service is missing.");
            _worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)
                ?? throw new InvalidOperationException("PresentationWorldHudBuffer service is missing.");
            _abilityDefinitions = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            _elapsedSeconds += MathF.Max(0f, dt);
            if (!IsRtsMapActive() ||
                !SelectionContextRuntime.TryGetCurrentPrimary(_engine.World, _engine.GlobalContext, out Entity selected) ||
                !_engine.World.IsAlive(selected) ||
                !TryResolveWorldPosition(selected, out Vector3 center))
            {
                return;
            }

            Vector4 accent = ResolveAccent(selected);
            bool hasQueue = _engine.World.TryGet(selected, out OrderBuffer orders) &&
                            (orders.HasActive || orders.QueuedCount > 0 || orders.HasPending);

            EmitSelectionRing(center, accent, pulseScale: 1f, alpha: 0.22f);
            if (hasQueue)
            {
                EmitSelectionRing(center, accent, pulseScale: 1.18f, alpha: 0.10f);
            }

            if (TryResolveProgress(selected, out float progressRatio))
            {
                EmitProgressBar(selected, center, accent, progressRatio);
            }
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EmitSelectionRing(Vector3 center, Vector4 accent, float pulseScale, float alpha)
        {
            float pulse = 1f + 0.06f * MathF.Sin(_elapsedSeconds * 5.4f);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Ring,
                Center = center + new Vector3(0f, 0.04f, 0f),
                Radius = 1.42f * pulseScale * pulse,
                InnerRadius = 1.1f * pulseScale,
                FillColor = new Vector4(accent.X, accent.Y, accent.Z, alpha * 0.4f),
                BorderColor = new Vector4(accent.X, accent.Y, accent.Z, alpha),
                BorderWidth = 0.05f
            });
        }

        private void EmitProgressBar(Entity owner, Vector3 center, Vector4 accent, float progressRatio)
        {
            int stableId = ResolveStableId(owner, WorldHudItemKind.Bar, discriminator: 1);
            if (stableId <= 0)
            {
                return;
            }

            Vector4 background = new(0.08f, 0.12f, 0.18f, 0.9f);
            Vector4 fill = new(accent.X, accent.Y, accent.Z, 0.95f);
            _worldHud.TryAdd(new WorldHudItem
            {
                StableId = stableId,
                DirtySerial = HudItemIdentity.ComposeBarDirtySerial(102f, 10f, progressRatio, in background, in fill),
                Kind = WorldHudItemKind.Bar,
                WorldPosition = center + new Vector3(0f, 2.1f, 0f),
                Width = 102f,
                Height = 10f,
                Value0 = progressRatio,
                Color0 = background,
                Color1 = fill
            });
        }

        private bool TryResolveProgress(Entity selected, out float progressRatio)
        {
            progressRatio = 0f;
            if (_engine.World.TryGet(selected, out AbilityExecInstance exec))
            {
                int totalTicks = ResolveExecTotalTicks(exec.AbilityId, exec.IsToggleDeactivating);
                if (totalTicks <= 0)
                {
                    totalTicks = Math.Max(exec.CurrentTick, 1);
                }

                progressRatio = Math.Clamp(exec.CurrentTick / (float)Math.Max(1, totalTicks), 0f, 1f);
                return true;
            }

            if (_engine.World.TryGet(selected, out ActiveEffectContainer effects))
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    Entity effectEntity = effects.GetEntity(i);
                    if (!_engine.World.IsAlive(effectEntity) ||
                        !_engine.World.TryGet(effectEntity, out GameplayEffect effect) ||
                        effect.TotalTicks <= 0 ||
                        effect.RemainingTicks <= 0)
                    {
                        continue;
                    }

                    progressRatio = Math.Clamp((effect.TotalTicks - effect.RemainingTicks) / (float)Math.Max(1, effect.TotalTicks), 0f, 1f);
                    return true;
                }
            }

            return false;
        }

        private int ResolveExecTotalTicks(int abilityId, bool isToggleDeactivating)
        {
            if (abilityId <= 0 || _abilityDefinitions == null || !_abilityDefinitions.TryGet(abilityId, out AbilityDefinition definition))
            {
                return 0;
            }

            AbilityExecSpec spec = isToggleDeactivating && definition.HasToggleSpec
                ? definition.ToggleSpec.DeactivateExecSpec
                : definition.ExecSpec;

            int total = 0;
            for (int i = 0; i < spec.ItemCount; i++)
            {
                int endTick = spec.GetTick(i) + Math.Max(0, spec.GetDurationTicks(i));
                if (endTick > total)
                {
                    total = endTick;
                }
            }

            return total;
        }

        private bool TryResolveWorldPosition(Entity selected, out Vector3 center)
        {
            if (_engine.World.TryGet(selected, out VisualTransform visual))
            {
                center = visual.Position;
                return true;
            }

            if (_engine.World.TryGet(selected, out WorldPositionCm worldPosition))
            {
                center = new Vector3(
                    worldPosition.Value.X.ToFloat() * 0.01f,
                    0f,
                    worldPosition.Value.Y.ToFloat() * 0.01f);
                return true;
            }

            center = default;
            return false;
        }

        private int ResolveStableId(Entity owner, WorldHudItemKind kind, int discriminator)
        {
            if (!_engine.World.TryGet(owner, out PresentationStableId stable))
            {
                return 0;
            }

            return HudItemIdentity.ComposeStableId(stable.Value, kind, discriminator);
        }

        private Vector4 ResolveAccent(Entity selected)
        {
            string name = _engine.World.TryGet(selected, out Name entityName)
                ? entityName.Value ?? string.Empty
                : string.Empty;

            if (name.Contains("Barracks", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(0.48f, 0.79f, 0.44f, 1f);
            }

            if (name.Contains("Factory", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(0.95f, 0.56f, 0.27f, 1f);
            }

            if (name.Contains("Gateway", StringComparison.OrdinalIgnoreCase))
            {
                return new Vector4(0.23f, 0.78f, 0.96f, 1f);
            }

            return new Vector4(0.35f, 0.72f, 1f, 1f);
        }

        private bool IsRtsMapActive()
        {
            var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
            if (tags == null)
            {
                return false;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], "rts", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tags[i], "rts_showcase", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
