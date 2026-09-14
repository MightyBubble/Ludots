using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using MobaDemoMod.Triggers;

namespace MobaDemoMod.Systems
{
    /// <summary>
    /// MOBA input policy (migration slice 2): the local order mapping installs through the
    /// CoreInputMod auto assembly; this policy owns the MOBA-specific surface — F1–F4 cast-mode
    /// switching, the mode HUD, and the aiming-state indicator presenter bridge.
    /// </summary>
    public sealed class MobaInputModeSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private InputOrderMappingSystem? _mapping;
        private bool _policyAttached;

        public MobaInputModeSystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_policyAttached)
            {
                TryAttachPolicy();
            }

            if (_mapping != null &&
                _globals.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var inputObj) &&
                inputObj is IInputActionReader input)
            {
                CheckModeSwitchKeys(input, _mapping);
            }

            RenderModeHud();
        }

        private void TryAttachPolicy()
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) ||
                mappingObj is not InputOrderMappingSystem mapping)
            {
                return;
            }

            _mapping = mapping;
            _policyAttached = true;

            // Aiming state -> range-circle presenter scope (AimCast mode).
            if (_globals.TryGetValue(CoreServiceKeys.PresenterCommandBuffer.Name, out var cmdObj) &&
                cmdObj is PresenterCommandBuffer commands)
            {
                var config = (MobaConfig)_globals[InstallMobaDemoOnGameStartTrigger.MobaConfigKey];
                var presenterDefinitions = _globals.TryGetValue(CoreServiceKeys.PresenterDefinitionRegistry.Name, out var prObj) &&
                    prObj is PresenterDefinitionRegistry definitions
                        ? definitions
                        : null;
                int rangeCircleDefId = presenterDefinitions?.GetId(config.Presentation.RangeCircleIndicatorDefKey) ?? 0;

                mapping.SetAimingStateChangedHandler((isAiming, aimMapping) =>
                {
                    int scopeId = aimMapping.ActionId.GetHashCode();
                    if (isAiming)
                    {
                        commands.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = rangeCircleDefId,
                            ScopeTag = scopeId,
                            Source = TryResolveAutoOrderSource(out var orderSource)
                                ? orderSource.GetControlledActor()
                                : default
                        });
                    }
                    else
                    {
                        commands.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.DestroyPresenterScope,
                            ScopeTag = scopeId
                        });
                    }
                });
            }
        }

        private bool TryResolveAutoOrderSource(out LocalOrderSourceHelper orderSource)
        {
            orderSource = _globals.TryGetValue(AutoInstalledLocalOrderSourceSystem.ServiceKey.Name, out var sourceObj) &&
                sourceObj is AutoInstalledLocalOrderSourceSystem autoSource
                    ? autoSource.OrderSource
                    : null!;
            return orderSource != null;
        }

        private static void CheckModeSwitchKeys(IInputActionReader input, InputOrderMappingSystem mapping)
        {
            if (input.PressedThisFrame("ModeWoW"))
            {
                mapping.SetInteractionMode(CastModeType.TargetFirst);
            }
            else if (input.PressedThisFrame("ModeLoL"))
            {
                mapping.SetInteractionMode(CastModeType.SmartCast);
            }
            else if (input.PressedThisFrame("ModeSC2"))
            {
                mapping.SetInteractionMode(CastModeType.AimCast);
            }
            else if (input.PressedThisFrame("ModeIndicator"))
            {
                mapping.SetInteractionMode(CastModeType.SmartCastWithIndicator);
            }
        }

        private void RenderModeHud()
        {
            if (_mapping == null) return;
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay) return;

            overlay.AddRect(
                x: 8,
                y: 8,
                width: 620,
                height: 74,
                fill: new System.Numerics.Vector4(0f, 0f, 0f, 0.45f),
                border: new System.Numerics.Vector4(1f, 1f, 1f, 0.16f));
            overlay.AddText(16, 16, $"Mode: {ToModeLabel(_mapping.InteractionMode)}", 20, new System.Numerics.Vector4(1f, 1f, 0.6f, 1f));
            overlay.AddText(16, 42, "F1 WoW(TargetFirst) | F2 LoL(SmartCast) | F3 SC2(AimCast) | F4 Indicator", 16, new System.Numerics.Vector4(0.78f, 0.92f, 1f, 1f));
        }

        private static string ToModeLabel(CastModeType mode)
        {
            return mode switch
            {
                CastModeType.TargetFirst => "WoW / target first",
                CastModeType.SmartCast => "LoL / smart cast",
                CastModeType.AimCast => "SC2 / aim then confirm",
                CastModeType.SmartCastWithIndicator => "LoL Indicator / hold to show, release to cast",
                _ => mode.ToString(),
            };
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
