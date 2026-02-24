using System;
using System.Threading.Tasks;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;
using Ludots.Core.Gameplay.GAS.Registry;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Mathematics;
using System.Numerics;
using System.Runtime.CompilerServices; // Added for Unsafe

using PerformanceVisualizationMod.Systems;
using Ludots.Core.Gameplay;
using Ludots.Core.Input.Runtime;

namespace PerformanceVisualizationMod.Triggers
{
    public class VisualBenchmarkTrigger : Trigger
    {
        private readonly IModContext _modContext;

        public VisualBenchmarkTrigger(IModContext modContext)
        {
            _modContext = modContext;
            EventKey = VisualBenchmarkEvents.RunVisualBenchmark;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            _modContext.Log("[VisualBenchmark] Starting simulation...");
            
            var world = context.GetWorld();
            if (world == null) return Task.CompletedTask;

            // Register Camera Controller
            var gameSession = context.Get<GameSession>(ContextKeys.GameSession);
            var inputHandler = context.Get<PlayerInputHandler>(ContextKeys.InputHandler);

            if (gameSession != null && inputHandler != null)
            {
                _modContext.Log("[VisualBenchmark] Registering Camera Controller...");
                var controller = new BenchmarkCameraController(inputHandler);
                gameSession.Camera.SetController(controller);
                // Correctly use Vector2
                gameSession.Camera.State.TargetCm = new Vector2(50000, 50000);
                gameSession.Camera.State.DistanceCm = 80000f;
            }

            // 1. Setup GAS Registry
            int healthId = AttributeRegistry.Register("Health");
            int manaId = AttributeRegistry.Register("Mana");
            int damageEventId = TagRegistry.Register("Event.DamageTaken");

            // New Attributes for Data-Driven Ability
            int cdAttrId = AttributeRegistry.Register("Ability.Cooldown");
            int costAttrId = AttributeRegistry.Register("Ability.Cost");
            int cdTagId = TagRegistry.Register("State.Cooldown");

            // 2. Create Template
            var abilityTemplateEntity = world.Create();
            world.Add(abilityTemplateEntity, new AbilityTemplate());
            world.Add(abilityTemplateEntity, new AbilityCooldown { CooldownValueAttributeId = cdAttrId, CooldownTagId = cdTagId });
            world.Add(abilityTemplateEntity, new AbilityCost { CostValueAttributeId = costAttrId, CostResourceAttributeId = manaId });
            var targetingEntity = world.Create();
            world.Add(targetingEntity, new TargetSelector { Shape = TargetShape.Single });
            var instructions = new InstructionBuffer();
            instructions.Add(OpCode.ModifyAttribute, healthId, 1.0f, SourceType.Constant);
            world.Add(abilityTemplateEntity, instructions);

            // 3. Create Entities with Visuals
            int entityCount = 100000; // 100k entities for visual test
            var rng = new Random();
            
            var archetype = new ComponentType[]
            {
                typeof(Position),
                typeof(WorldPositionCm),
                typeof(VisualTransform), // Core will sync this
                typeof(VisualModel),     // Core will use this for culling/rendering
                typeof(CullState),       // Core updates this
                typeof(AttributeBuffer),
                typeof(ActiveEffectContainer),
                typeof(GameplayTagContainer),
                typeof(TagCountContainer),
                typeof(AbilityStateBuffer),
                typeof(ReactionBuffer)
            };

            for (int i = 0; i < entityCount; i++)
            {
                var e = world.Create(archetype);

                int xCm = rng.Next(0, 100000);
                int yCm = rng.Next(0, 100000);
                world.Set(e, new WorldPositionCm { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) });
                world.Set(e, new Position { GridPos = new IntVector2(xCm / 100, yCm / 100) });
                
                // Visual Model (Primitive ID 1 = Cube/Sphere)
                world.Set(e, new VisualModel { MeshId = 1, MaterialId = 1, BaseScale = 1.0f });
                world.Set(e, VisualTransform.Default); // Init

                // GAS State
                ref var attr = ref world.Get<AttributeBuffer>(e);
                attr.SetBase(healthId, 100f);
                attr.SetBase(manaId, 100f);
                attr.SetCurrent(healthId, 100f);
                attr.SetCurrent(manaId, 100f);

                // Set Ability Data
                attr.SetBase(cdAttrId, 1.0f);
                attr.SetCurrent(cdAttrId, 1.0f);
                attr.SetBase(costAttrId, 5.0f);
                attr.SetCurrent(costAttrId, 5.0f);

                ref var abilities = ref world.Get<AbilityStateBuffer>(e);
                abilities.AddAbility(abilityTemplateEntity);

                ref var reactions = ref world.Get<ReactionBuffer>(e);
                reactions.Add(damageEventId, 0);
            }

            // 4. Start Event Loop (Async or hooked into Update)
            var source = world.Create();
            // Spawn initial events to kickstart reactions
            for (int i = 0; i < 100; i++)
            {
                // Target random entities
            }

            _modContext.Log($"[VisualBenchmark] Created {entityCount} visual entities.");
            return Task.CompletedTask;
        }
    }
}
