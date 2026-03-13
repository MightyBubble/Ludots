using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcaseOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 PanelFill = new(0.05f, 0.08f, 0.16f, 0.78f);
        private static readonly Vector4 PanelBorder = new(0.24f, 0.72f, 1.00f, 0.45f);
        private static readonly Vector4 TitleColor = new(0.98f, 0.96f, 0.68f, 1.00f);
        private static readonly Vector4 TextColor = new(0.86f, 0.92f, 0.98f, 1.00f);
        private static readonly Vector4 HintColor = new(0.60f, 0.72f, 0.82f, 0.92f);

        private readonly GameEngine _engine;

        public InteractionShowcaseOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        public void Update(in float dt)
        {
            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!InteractionShowcaseIds.IsShowcaseMap(mapId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            string scenarioId = ReadString(InteractionShowcaseRuntimeKeys.ActiveScenarioId, InteractionShowcaseIds.ResolveScenarioId(mapId));
            if (string.Equals(scenarioId, InteractionShowcaseIds.C1HostileUnitDamageScenarioId, System.StringComparison.OrdinalIgnoreCase))
            {
                DrawC1Overlay(overlay);
                return;
            }

            if (string.Equals(scenarioId, InteractionShowcaseIds.C2FriendlyUnitHealScenarioId, System.StringComparison.OrdinalIgnoreCase))
            {
                DrawC2Overlay(overlay);
                return;
            }

            if (string.Equals(scenarioId, InteractionShowcaseIds.C3AnyUnitConditionalScenarioId, System.StringComparison.OrdinalIgnoreCase))
            {
                DrawC3Overlay(overlay);
                return;
            }

            DrawB1Overlay(overlay);
        }

        private void DrawB1Overlay(ScreenOverlayBuffer overlay)
        {
            string stage = ReadString(InteractionShowcaseRuntimeKeys.Stage, "warmup");
            int tick = _engine.GameSession.CurrentTick;
            int scriptTick = ReadInt(InteractionShowcaseRuntimeKeys.ScriptTick, 0);
            float attackDamage = ReadFloat(InteractionShowcaseRuntimeKeys.HeroAttackDamage, 0f);
            float mana = ReadFloat(InteractionShowcaseRuntimeKeys.HeroMana, 0f);
            bool castSubmitted = ReadBool(InteractionShowcaseRuntimeKeys.CastSubmitted);
            bool empowered = ReadBool(InteractionShowcaseRuntimeKeys.HeroEmpoweredTag);
            int empoweredCount = ReadInt(InteractionShowcaseRuntimeKeys.HeroEmpoweredTagCount, 0);
            bool buffExpired = ReadBool(InteractionShowcaseRuntimeKeys.BuffExpired);
            string failReason = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            string failAttribute = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailAttribute, "-");
            float failDelta = ReadFloat(InteractionShowcaseRuntimeKeys.LastCastFailDelta, 0f);
            int failTick = ReadInt(InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);

            overlay.AddRect(16, 16, 560, 168, PanelFill, PanelBorder);
            overlay.AddText(30, 40, "Interaction Showcase | B1 Self Buff", 18, TitleColor);
            overlay.AddText(30, 66, $"Stage={stage}  ScriptTick={scriptTick}  Tick={tick}", 15, TextColor);
            overlay.AddText(30, 90, $"AttackDamage={attackDamage:F1}  Mana={mana:F1}  CastSubmitted={castSubmitted}", 15, TextColor);
            overlay.AddText(30, 114, $"EffectiveTag={empowered}  EmpoweredCount={empoweredCount}  BuffExpired={buffExpired}", 15, TextColor);
            overlay.AddText(30, 138, $"LastFail={failReason}  Attribute={failAttribute}  Delta={failDelta:F1}  Tick={failTick}", 15, TextColor);
            overlay.AddText(30, 162, "Autoplay drives slot 0 via OrderQueue for deterministic evidence.", 13, HintColor);
        }

        private void DrawC1Overlay(ScreenOverlayBuffer overlay)
        {
            string stage = ReadString(InteractionShowcaseRuntimeKeys.Stage, "warmup");
            int tick = _engine.GameSession.CurrentTick;
            int scriptTick = ReadInt(InteractionShowcaseRuntimeKeys.ScriptTick, 0);
            float baseDamage = ReadFloat(InteractionShowcaseRuntimeKeys.HeroBaseDamage, 0f);
            float mana = ReadFloat(InteractionShowcaseRuntimeKeys.HeroMana, 0f);
            float primaryHealth = ReadFloat(InteractionShowcaseRuntimeKeys.PrimaryTargetHealth, 0f);
            float primaryArmor = ReadFloat(InteractionShowcaseRuntimeKeys.PrimaryTargetArmor, 0f);
            float damageAmount = ReadFloat(InteractionShowcaseRuntimeKeys.DamageAmount, 0f);
            float finalDamage = ReadFloat(InteractionShowcaseRuntimeKeys.FinalDamage, 0f);
            float invalidHealth = ReadFloat(InteractionShowcaseRuntimeKeys.InvalidTargetHealth, 0f);
            float farHealth = ReadFloat(InteractionShowcaseRuntimeKeys.FarTargetHealth, 0f);
            bool castSubmitted = ReadBool(InteractionShowcaseRuntimeKeys.CastSubmitted);
            bool damageApplied = ReadBool(InteractionShowcaseRuntimeKeys.DamageApplied);
            int damageAppliedTick = ReadInt(InteractionShowcaseRuntimeKeys.DamageAppliedTick, -1);
            string lastFail = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            string lastTarget = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, "-");

            overlay.AddRect(16, 16, 680, 192, PanelFill, PanelBorder);
            overlay.AddText(30, 40, "Interaction Showcase | C1 Hostile Unit Damage", 18, TitleColor);
            overlay.AddText(30, 66, $"Stage={stage}  ScriptTick={scriptTick}  Tick={tick}  CastSubmitted={castSubmitted}", 15, TextColor);
            overlay.AddText(30, 90, $"HeroBaseDamage={baseDamage:F1}  Mana={mana:F1}  DamageApplied={damageApplied}  DamageTick={damageAppliedTick}", 15, TextColor);
            overlay.AddText(30, 114, $"PrimaryHP={primaryHealth:F1}  Armor={primaryArmor:F1}  DamageAmount={damageAmount:F1}  FinalDamage={finalDamage:F1}", 15, TextColor);
            overlay.AddText(30, 138, $"InvalidTargetHP={invalidHealth:F1}  FarTargetHP={farHealth:F1}", 15, TextColor);
            overlay.AddText(30, 162, $"LastResult={lastFail}  Target={lastTarget}", 15, TextColor);
            overlay.AddText(30, 186, "Autoplay validates hostile/in-range locally, then drives slot 0 through OrderQueue.", 13, HintColor);
        }

        private void DrawC2Overlay(ScreenOverlayBuffer overlay)
        {
            string stage = ReadString(InteractionShowcaseRuntimeKeys.Stage, "warmup");
            int tick = _engine.GameSession.CurrentTick;
            int scriptTick = ReadInt(InteractionShowcaseRuntimeKeys.ScriptTick, 0);
            float mana = ReadFloat(InteractionShowcaseRuntimeKeys.HeroMana, 0f);
            float allyHealth = ReadFloat(InteractionShowcaseRuntimeKeys.C2AllyTargetHealth, 0f);
            float hostileHealth = ReadFloat(InteractionShowcaseRuntimeKeys.C2HostileTargetHealth, 0f);
            float deadAllyHealth = ReadFloat(InteractionShowcaseRuntimeKeys.C2DeadAllyTargetHealth, 0f);
            float healAmount = ReadFloat(InteractionShowcaseRuntimeKeys.C2HealAmount, 0f);
            bool healApplied = ReadBool(InteractionShowcaseRuntimeKeys.C2HealApplied);
            int healAppliedTick = ReadInt(InteractionShowcaseRuntimeKeys.C2HealAppliedTick, -1);
            bool castSubmitted = ReadBool(InteractionShowcaseRuntimeKeys.CastSubmitted);
            string lastFail = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            string lastTarget = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, "-");

            overlay.AddRect(16, 16, 720, 192, PanelFill, PanelBorder);
            overlay.AddText(30, 40, "Interaction Showcase | C2 Friendly Unit Heal", 18, TitleColor);
            overlay.AddText(30, 66, $"Stage={stage}  ScriptTick={scriptTick}  Tick={tick}  CastSubmitted={castSubmitted}", 15, TextColor);
            overlay.AddText(30, 90, $"HeroMana={mana:F1}  HealApplied={healApplied}  HealTick={healAppliedTick}  HealAmount={healAmount:F1}", 15, TextColor);
            overlay.AddText(30, 114, $"AllyHP={allyHealth:F1}  HostileHP={hostileHealth:F1}  DeadAllyHP={deadAllyHealth:F1}", 15, TextColor);
            overlay.AddText(30, 138, $"LastResult={lastFail}  Target={lastTarget}", 15, TextColor);
            overlay.AddText(30, 162, "Positive branch drives slot 0 through OrderQueue; hostile/dead branches are blocked locally before enqueue.", 13, HintColor);
        }

        private void DrawC3Overlay(ScreenOverlayBuffer overlay)
        {
            string stage = ReadString(InteractionShowcaseRuntimeKeys.Stage, "warmup");
            int tick = _engine.GameSession.CurrentTick;
            int scriptTick = ReadInt(InteractionShowcaseRuntimeKeys.ScriptTick, 0);
            float mana = ReadFloat(InteractionShowcaseRuntimeKeys.HeroMana, 0f);
            float hostileMoveSpeed = ReadFloat(InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed, 0f);
            float friendlyMoveSpeed = ReadFloat(InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed, 0f);
            bool hostileApplied = ReadBool(InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied);
            int hostileAppliedTick = ReadInt(InteractionShowcaseRuntimeKeys.C3HostilePolymorphAppliedTick, -1);
            int hostileCount = ReadInt(InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount, 0);
            bool hostileActive = ReadBool(InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive);
            bool friendlyApplied = ReadBool(InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied);
            int friendlyAppliedTick = ReadInt(InteractionShowcaseRuntimeKeys.C3FriendlyHasteAppliedTick, -1);
            int friendlyCount = ReadInt(InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount, 0);
            bool friendlyActive = ReadBool(InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive);
            bool castSubmitted = ReadBool(InteractionShowcaseRuntimeKeys.CastSubmitted);
            string lastFail = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            string lastTarget = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, "-");

            overlay.AddRect(16, 16, 860, 216, PanelFill, PanelBorder);
            overlay.AddText(30, 40, "Interaction Showcase | C3 Any Unit Conditional", 18, TitleColor);
            overlay.AddText(30, 66, $"Stage={stage}  ScriptTick={scriptTick}  Tick={tick}  CastSubmitted={castSubmitted}", 15, TextColor);
            overlay.AddText(30, 90, $"HeroMana={mana:F1}  HostileApplied={hostileApplied}  HostileTick={hostileAppliedTick}  HostileTagCount={hostileCount}", 15, TextColor);
            overlay.AddText(30, 114, $"HostileMoveSpeed={hostileMoveSpeed:F1}  HostileTagActive={hostileActive}  FriendlyApplied={friendlyApplied}  FriendlyTick={friendlyAppliedTick}", 15, TextColor);
            overlay.AddText(30, 138, $"FriendlyMoveSpeed={friendlyMoveSpeed:F1}  FriendlyTagActive={friendlyActive}  FriendlyTagCount={friendlyCount}", 15, TextColor);
            overlay.AddText(30, 162, $"LastResult={lastFail}  Target={lastTarget}", 15, TextColor);
            overlay.AddText(30, 186, "One ability emits hostile/friendly effects together; relationFilter decides which branch lands.", 13, HintColor);
        }

        private string ReadString(string key, string fallback)
        {
            return _engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
        }

        private int ReadInt(string key, int fallback)
        {
            return _engine.GlobalContext.TryGetValue(key, out var value) && value is int number
                ? number
                : fallback;
        }

        private float ReadFloat(string key, float fallback)
        {
            return _engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : fallback;
        }

        private bool ReadBool(string key)
        {
            return _engine.GlobalContext.TryGetValue(key, out var value) && value is bool flag && flag;
        }
    }
}
