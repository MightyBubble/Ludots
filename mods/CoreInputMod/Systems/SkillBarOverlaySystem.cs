using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    public sealed class SkillBarOverlaySystem : ISystem<float>
    {
        public const string SkillBarKeyLabelsKey = "CoreInputMod.SkillBarKeyLabels";
        public const string SkillBarEnabledKey = "CoreInputMod.SkillBarEnabled";

        private const int SlotWidth = 52;
        private const int SlotHeight = 52;
        private const int SlotGap = 6;
        private const int BottomMargin = 12;

        private static readonly Vector4 SlotBackground = new(0.12f, 0.12f, 0.16f, 0.85f);
        private static readonly Vector4 SlotBorder = new(0.4f, 0.4f, 0.5f, 0.6f);
        private static readonly Vector4 SlotActive = new(0.2f, 0.5f, 0.9f, 0.25f);
        private static readonly Vector4 KeyColor = new(0.9f, 0.9f, 0.9f, 0.9f);
        private static readonly Vector4 AbilityColor = new(0.75f, 0.85f, 1f, 0.95f);

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public SkillBarOverlaySystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (_globals.TryGetValue(SkillBarEnabledKey, out var enabledObj) && enabledObj is bool enabled && !enabled)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            var entity = GetControlledEntity();
            if (!_world.IsAlive(entity) || !_world.Has<AbilityStateBuffer>(entity))
            {
                return;
            }

            ref var abilityBuffer = ref _world.Get<AbilityStateBuffer>(entity);
            if (abilityBuffer.Count <= 0)
            {
                return;
            }

            string[]? keyLabels = _globals.TryGetValue(SkillBarKeyLabelsKey, out var labelsObj) && labelsObj is string[] labels
                ? labels
                : null;

            int screenWidth = 1280;
            int screenHeight = 720;
            if (_globals.TryGetValue(CoreServiceKeys.ViewController.Name, out var viewObj) && viewObj is IViewController view)
            {
                screenWidth = (int)view.Resolution.X;
                screenHeight = (int)view.Resolution.Y;
            }

            int totalWidth = abilityBuffer.Count * SlotWidth + (abilityBuffer.Count - 1) * SlotGap;
            int x0 = (screenWidth - totalWidth) / 2;
            int y0 = screenHeight - SlotHeight - BottomMargin;

            for (int index = 0; index < abilityBuffer.Count; index++)
            {
                int x = x0 + index * (SlotWidth + SlotGap);
                var slot = abilityBuffer.Get(index);
                bool hasAbility = slot.AbilityId > 0 || slot.TemplateEntityId > 0;

                overlay.AddRect(x, y0, SlotWidth, SlotHeight, SlotBackground, SlotBorder);
                if (hasAbility)
                {
                    overlay.AddRect(x + 1, y0 + 1, SlotWidth - 2, SlotHeight - 2, SlotActive, default);
                }

                string keyLabel = keyLabels != null && index < keyLabels.Length ? keyLabels[index] : (index + 1).ToString();
                overlay.AddText(x + 4, y0 + SlotHeight - 18, keyLabel, 14, KeyColor);
                overlay.AddText(x + 6, y0 + 6, hasAbility ? $"{index + 1}" : "-", 18, AbilityColor);
            }
        }

        private Entity GetControlledEntity()
        {
            if (_globals.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out var selectedObj) &&
                selectedObj is Entity selected &&
                _world.IsAlive(selected) &&
                _world.Has<AbilityStateBuffer>(selected))
            {
                return selected;
            }

            if (_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) && localObj is Entity local)
            {
                return local;
            }

            return default;
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
