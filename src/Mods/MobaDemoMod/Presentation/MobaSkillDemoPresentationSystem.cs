using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using MobaDemoMod.Triggers;

namespace MobaDemoMod.Presentation
{
    /// <summary>
    /// Skill demo presenter:
    /// 1) 手动演示（F9/F10/F11）；
    /// 2) 展台自动演示（F6 开关、F7 重置）；
    /// 3) 所有效果反馈统一通过 Performer 指令输出。
    /// </summary>
    public sealed class MobaSkillDemoPresentationSystem : ISystem<float>
    {
        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly IModContext _ctx;

        private bool _initialized;
        private MobaConfig? _cfg;
        private bool _showcaseEnabled;
        private float _showcaseTimer;
        private int _showcaseStep;
        private int _scopeSerial = 99500;

        private Entity _hero;
        private Entity _enemyA;
        private Entity _enemyB;

        private int _fireballEffectId;
        private int _magicCircleEffectId;
        private int _summonEffectId;
        private int _displacementEffectId;

        public MobaSkillDemoPresentationSystem(World world, Dictionary<string, object> globals, IModContext ctx)
        {
            _world = world;
            _globals = globals;
            _ctx = ctx;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!EnsureInitialized()) return;

            ResolveAnchors();
            HandleManualHotkeys();
            ConsumeGasPresentationEvents();
            TickShowcase(dt);
            RenderDemoHud();
        }

        private bool EnsureInitialized()
        {
            if (_initialized) return true;
            _initialized = true;

            if (!_globals.TryGetValue(InstallMobaDemoOnGameStartTrigger.MobaConfigKey, out var cfgObj) || cfgObj is not MobaConfig cfg)
            {
                _ctx.Log("[MobaDemoMod] SkillDemo: missing MobaConfig, demo system disabled.");
                return false;
            }

            _cfg = cfg;
            _showcaseEnabled = cfg.SkillDemo.ShowcaseEnabledOnStart;
            _showcaseTimer = 0.2f;
            _showcaseStep = 0;

            _fireballEffectId = ResolveEffectId(cfg.SkillDemo.ManualFireballEffectId);
            _magicCircleEffectId = ResolveEffectId(cfg.SkillDemo.ManualMagicCircleEffectId);
            _summonEffectId = ResolveEffectId(cfg.SkillDemo.ManualSummonEffectId);
            _displacementEffectId = EffectTemplateIdRegistry.GetId("Effect.Moba.Displacement.R");

            _ctx.Log("[MobaDemoMod] SkillDemo initialized.");
            return true;
        }

        private int ResolveEffectId(string effectName)
        {
            int id = EffectTemplateIdRegistry.GetId(effectName);
            if (id <= 0)
                throw new InvalidOperationException($"[MobaDemoMod] SkillDemo effect '{effectName}' is not registered.");
            return id;
        }

        private void ResolveAnchors()
        {
            if (!_world.IsAlive(_hero)) _hero = FindByName("Hero");
            if (!_world.IsAlive(_enemyA)) _enemyA = FindByName("Enemy1");
            if (!_world.IsAlive(_enemyB)) _enemyB = FindByName("Enemy2");

            if (_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var lpObj) && lpObj is Entity lp && _world.IsAlive(lp))
            {
                _hero = lp;
            }
        }

        private Entity FindByName(string name)
        {
            var q = new QueryDescription().WithAll<Name>();
            Entity found = default;
            _world.Query(in q, (Entity e, ref Name n) =>
            {
                if (found != default) return;
                if (string.Equals(n.Value, name, StringComparison.OrdinalIgnoreCase))
                    found = e;
            });
            return found;
        }

        private void HandleManualHotkeys()
        {
            if (!_globals.TryGetValue(ContextKeys.InputHandler, out var inputObj) || inputObj is not PlayerInputHandler input)
                return;

            if (input.PressedThisFrame("DemoShowcaseToggle"))
            {
                _showcaseEnabled = !_showcaseEnabled;
                _showcaseTimer = 0.1f;
            }

            if (input.PressedThisFrame("DemoShowcaseReset"))
            {
                _showcaseStep = 0;
                _showcaseTimer = 0.1f;
                HighlightShowcaseTarget(_enemyA);
            }

            if (input.PressedThisFrame("DemoFireball"))
                TriggerFireball();
            if (input.PressedThisFrame("DemoMagicCircle"))
                TriggerMagicCircle();
            if (input.PressedThisFrame("DemoSummon"))
                TriggerSummon();
        }

        private void ConsumeGasPresentationEvents()
        {
            if (!_globals.TryGetValue(ContextKeys.GasPresentationEventBuffer, out var geObj) || geObj is not GasPresentationEventBuffer gasEvents)
                return;
            if (!_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) || cmdObj is not PresentationCommandBuffer commands)
                return;
            if (_cfg == null) return;

            var span = gasEvents.Events;
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var ge = ref span[i];

                if (ge.Kind == GasPresentationEventKind.CastCommitted && ge.Actor == _hero)
                {
                    switch (ge.AbilitySlot)
                    {
                        case 0:
                            EmitPerformer(commands, _cfg.Presentation.DemoFireballDefId, ge.Actor);
                            if (_world.IsAlive(ge.Target)) EmitPerformer(commands, _cfg.Presentation.DemoFireballDefId, ge.Target);
                            break;
                        case 1:
                            EmitPerformer(commands, _cfg.Presentation.DemoMagicCircleDefId, ge.Actor);
                            break;
                        case 3:
                            EmitPerformer(commands, _cfg.Presentation.DemoSummonDefId, ge.Actor);
                            break;
                    }
                }

                if (ge.Kind == GasPresentationEventKind.EffectApplied && ge.Delta < 0f && _world.IsAlive(ge.Target))
                {
                    EmitPerformer(commands, _cfg.Presentation.DemoFireballDefId, ge.Target);
                }
            }
        }

        private void TickShowcase(float dt)
        {
            if (!_showcaseEnabled || _cfg == null) return;
            if (!_world.IsAlive(_hero)) return;

            _showcaseTimer -= Math.Max(0f, dt);
            if (_showcaseTimer > 0f) return;

            _showcaseTimer = Math.Max(0.8f, _cfg.SkillDemo.ShowcaseStepIntervalSeconds);
            RunShowcaseStep();
            _showcaseStep = (_showcaseStep + 1) % 5;
        }

        private void RunShowcaseStep()
        {
            switch (_showcaseStep)
            {
                case 0:
                    HighlightShowcaseTarget(_enemyA);
                    break;
                case 1:
                    TriggerFireball();
                    break;
                case 2:
                    HighlightShowcaseTarget(_enemyB);
                    TriggerMagicCircle();
                    break;
                case 3:
                    TriggerSummon();
                    break;
                case 4:
                    TriggerDisplacement();
                    break;
            }
        }

        private void TriggerFireball()
        {
            if (!_world.IsAlive(_hero) || !_world.IsAlive(_enemyA)) return;
            PublishEffect(_fireballEffectId, _hero, _enemyA);
            if (_cfg == null) return;
            if (_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer commands)
            {
                EmitPerformer(commands, _cfg.Presentation.DemoFireballDefId, _hero);
            }
        }

        private void TriggerMagicCircle()
        {
            if (!_world.IsAlive(_hero)) return;
            var target = _world.IsAlive(_enemyB) ? _enemyB : _hero;
            PublishEffect(_magicCircleEffectId, _hero, target);
            if (_cfg == null) return;
            if (_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer commands)
            {
                EmitPerformer(commands, _cfg.Presentation.DemoMagicCircleDefId, target);
            }
        }

        private void TriggerSummon()
        {
            if (!_world.IsAlive(_hero)) return;
            PublishEffect(_summonEffectId, _hero, _hero);
            if (_cfg == null) return;
            if (_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) && cmdObj is PresentationCommandBuffer commands)
            {
                EmitPerformer(commands, _cfg.Presentation.DemoSummonDefId, _hero);
            }
        }

        private void TriggerDisplacement()
        {
            if (_displacementEffectId <= 0 || !_world.IsAlive(_hero) || !_world.IsAlive(_enemyA)) return;
            PublishEffect(_displacementEffectId, _hero, _enemyA);
        }

        private void PublishEffect(int templateId, Entity source, Entity target)
        {
            if (templateId <= 0) return;
            if (!_globals.TryGetValue(ContextKeys.EffectRequestQueue, out var reqObj) || reqObj is not EffectRequestQueue requests)
                return;

            requests.Publish(new EffectRequest
            {
                RootId = 0,
                Source = source,
                Target = target,
                TargetContext = default,
                TemplateId = templateId
            });
        }

        private void HighlightShowcaseTarget(Entity target)
        {
            if (_cfg == null) return;
            if (!_globals.TryGetValue(ContextKeys.PresentationCommandBuffer, out var cmdObj) || cmdObj is not PresentationCommandBuffer commands)
                return;

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.DestroyPerformerScope,
                IdA = _cfg.SkillDemo.ShowcaseScopeId
            });

            if (_world.IsAlive(target))
            {
                commands.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.CreatePerformer,
                    IdA = _cfg.Presentation.DemoTargetDefId,
                    IdB = _cfg.SkillDemo.ShowcaseScopeId,
                    Source = target
                });
            }
        }

        private void EmitPerformer(PresentationCommandBuffer commands, int performerDefId, Entity owner)
        {
            if (performerDefId <= 0 || !_world.IsAlive(owner)) return;
            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.CreatePerformer,
                IdA = performerDefId,
                IdB = NextScopeId(),
                Source = owner
            });
        }

        private int NextScopeId() => _scopeSerial++;

        private void RenderDemoHud()
        {
            if (_cfg == null) return;
            if (!_globals.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
                return;

            const int panelWidth = 540;
            const int panelHeight = 88;
            int x = 8;
            int y = 112;

            overlay.AddRect(
                x: x,
                y: y,
                width: panelWidth,
                height: panelHeight,
                fill: new Vector4(0f, 0f, 0f, 0.42f),
                border: new Vector4(1f, 1f, 1f, 0.14f));

            string showcaseState = _showcaseEnabled ? "ON" : "OFF";
            overlay.AddText(x + 10, y + 8, $"Skill Showcase: {showcaseState} | Step: {GetStepLabel(_showcaseStep)}", 18, new Vector4(1f, 0.92f, 0.5f, 1f));
            overlay.AddText(x + 10, y + 32, "F6 Toggle Showcase | F7 Reset Showcase", 16, new Vector4(0.86f, 0.95f, 1f, 1f));
            overlay.AddText(x + 10, y + 54, "F9 Fireball | F10 Magic Circle | F11 Summon", 16, new Vector4(0.82f, 1f, 0.82f, 1f));
        }

        private static string GetStepLabel(int step)
        {
            return step switch
            {
                0 => "Target Ready",
                1 => "Fireball",
                2 => "Magic Circle",
                3 => "Summon",
                4 => "Displacement",
                _ => "Idle"
            };
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
