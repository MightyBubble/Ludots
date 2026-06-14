using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using ChampionSkillSandboxMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;

namespace ChampionSkillSandboxMod.Systems
{
    internal sealed class ChampionSkillStressSpawnSystem : ISystem<float>
    {
        private const int FormationDepthColumns = 2;
        private const int FormationRowSpacingCm = 132;
        private const int FormationDepthSpacingCm = 112;
        private const int FormationDepthStaggerCm = 52;
        private const int TeamALeftX = 760;
        private const int TeamBRightX = 3640;
        private const int TeamCenterY = 1320;
        private const int WarriorOffsetCm = 0;
        private const int FireMageOffsetCm = 340;
        private const int LaserMageOffsetCm = 680;
        private const int PriestOffsetCm = 1020;

        private static readonly QueryDescription StressUnitQuery = new QueryDescription()
            .WithAll<Name, Team, MapEntity, AbilityStateBuffer>();

        private readonly GameEngine _engine;
        private readonly RuntimeEntitySpawnQueue _spawnQueue;
        private readonly ChampionSkillStressControlState _control;
        private readonly ChampionSkillStressTelemetry _telemetry;
        private readonly List<Entity> _teamAWarriors = new();
        private readonly List<Entity> _teamAFireMages = new();
        private readonly List<Entity> _teamALaserMages = new();
        private readonly List<Entity> _teamAPriests = new();
        private readonly List<Entity> _teamBWarriors = new();
        private readonly List<Entity> _teamBFireMages = new();
        private readonly List<Entity> _teamBLaserMages = new();
        private readonly List<Entity> _teamBPriests = new();

        public ChampionSkillStressSpawnSystem(
            GameEngine engine,
            RuntimeEntitySpawnQueue spawnQueue,
            ChampionSkillStressControlState control,
            ChampionSkillStressTelemetry telemetry)
        {
            _engine = engine;
            _spawnQueue = spawnQueue;
            _control = control;
            _telemetry = telemetry;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!ChampionSkillSandboxIds.IsStressMap(_engine.CurrentMapSession?.MapId.Value))
            {
                _telemetry.Reset();
                return;
            }

            _telemetry.IsActive = true;
            CollectUnits();
            MaintainTeam(
                mapId: _engine.CurrentMapSession!.MapId,
                desiredTotal: _control.DesiredTeamA,
                warriors: _teamAWarriors,
                fireMages: _teamAFireMages,
                laserMages: _teamALaserMages,
                priests: _teamAPriests,
                warriorTemplateId: "champion_skill_stress_team_a_warrior",
                fireMageTemplateId: "champion_skill_stress_team_a_fire_mage",
                laserMageTemplateId: "champion_skill_stress_team_a_laser_mage",
                priestTemplateId: "champion_skill_stress_team_a_priest",
                teamA: true);
            MaintainTeam(
                mapId: _engine.CurrentMapSession!.MapId,
                desiredTotal: _control.DesiredTeamB,
                warriors: _teamBWarriors,
                fireMages: _teamBFireMages,
                laserMages: _teamBLaserMages,
                priests: _teamBPriests,
                warriorTemplateId: "champion_skill_stress_team_b_warrior",
                fireMageTemplateId: "champion_skill_stress_team_b_fire_mage",
                laserMageTemplateId: "champion_skill_stress_team_b_laser_mage",
                priestTemplateId: "champion_skill_stress_team_b_priest",
                teamA: false);

            _telemetry.DesiredTeamA = _control.DesiredTeamA;
            _telemetry.DesiredTeamB = _control.DesiredTeamB;
            _telemetry.LiveTeamA = _teamAWarriors.Count + _teamAFireMages.Count + _teamALaserMages.Count + _teamAPriests.Count;
            _telemetry.LiveTeamB = _teamBWarriors.Count + _teamBFireMages.Count + _teamBLaserMages.Count + _teamBPriests.Count;
        }

        private void CollectUnits()
        {
            _teamAWarriors.Clear();
            _teamAFireMages.Clear();
            _teamALaserMages.Clear();
            _teamAPriests.Clear();
            _teamBWarriors.Clear();
            _teamBFireMages.Clear();
            _teamBLaserMages.Clear();
            _teamBPriests.Clear();

            string mapId = _engine.CurrentMapSession!.MapId.Value;
            _engine.World.Query(in StressUnitQuery, (Entity entity, ref Name name, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer _) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.Ordinal))
                {
                    return;
                }

                if (team.Id == 1)
                {
                    ResolveBucket(name.Value, _teamAWarriors, _teamAFireMages, _teamALaserMages, _teamAPriests).Add(entity);
                }
                else if (team.Id == 2)
                {
                    ResolveBucket(name.Value, _teamBWarriors, _teamBFireMages, _teamBLaserMages, _teamBPriests).Add(entity);
                }
            });
        }

        private static List<Entity> ResolveBucket(
            string name,
            List<Entity> warriors,
            List<Entity> fireMages,
            List<Entity> laserMages,
            List<Entity> priests)
        {
            if (name.Contains("FireMage", StringComparison.Ordinal))
            {
                return fireMages;
            }

            if (name.Contains("LaserMage", StringComparison.Ordinal))
            {
                return laserMages;
            }

            if (name.Contains("Priest", StringComparison.Ordinal))
            {
                return priests;
            }

            return warriors;
        }

        private void MaintainTeam(
            MapId mapId,
            int desiredTotal,
            List<Entity> warriors,
            List<Entity> fireMages,
            List<Entity> laserMages,
            List<Entity> priests,
            string warriorTemplateId,
            string fireMageTemplateId,
            string laserMageTemplateId,
            string priestTemplateId,
            bool teamA)
        {
            ResolveRoleTargets(desiredTotal, out int warriorTarget, out int fireMageTarget, out int laserMageTarget, out int priestTarget);
            TrimExcess(warriors, warriorTarget);
            TrimExcess(fireMages, fireMageTarget);
            TrimExcess(laserMages, laserMageTarget);
            TrimExcess(priests, priestTarget);

            EnqueueMissing(mapId, warriors.Count, warriorTarget, warriorTemplateId, teamA, StressRole.Warrior);
            EnqueueMissing(mapId, fireMages.Count, fireMageTarget, fireMageTemplateId, teamA, StressRole.FireMage);
            EnqueueMissing(mapId, laserMages.Count, laserMageTarget, laserMageTemplateId, teamA, StressRole.LaserMage);
            EnqueueMissing(mapId, priests.Count, priestTarget, priestTemplateId, teamA, StressRole.Priest);
        }

        private void TrimExcess(List<Entity> list, int desired)
        {
            for (int i = list.Count - 1; i >= desired; i--)
            {
                Entity entity = list[i];
                if (_engine.World.IsAlive(entity))
                {
                    _engine.World.Destroy(entity);
                }
            }
        }

        private void EnqueueMissing(MapId mapId, int liveCount, int desiredCount, string templateId, bool teamA, StressRole role)
        {
            for (int i = liveCount; i < desiredCount; i++)
            {
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = templateId,
                    WorldPositionCm = ComputeSpawnPosition(teamA, role, i, desiredCount),
                    HasWorldPosition = 1,
                    MapId = mapId,
                };

                if (!_spawnQueue.TryEnqueue(in request))
                {
                    throw new InvalidOperationException("ChampionSkillSandbox stress spawn failed to enqueue runtime entity spawn request.");
                }
            }
        }

        private static void ResolveRoleTargets(int desiredTotal, out int warriorTarget, out int fireMageTarget, out int laserMageTarget, out int priestTarget)
        {
            warriorTarget = Math.Max(2, desiredTotal * 40 / 100);
            fireMageTarget = Math.Max(2, desiredTotal * 24 / 100);
            laserMageTarget = Math.Max(2, desiredTotal * 22 / 100);
            priestTarget = Math.Max(2, desiredTotal - warriorTarget - fireMageTarget - laserMageTarget);

            int overflow = warriorTarget + fireMageTarget + laserMageTarget + priestTarget - desiredTotal;
            if (overflow > 0)
            {
                priestTarget = Math.Max(2, priestTarget - overflow);
            }
        }

        private static Fix64Vec2 ComputeSpawnPosition(bool teamA, StressRole role, int index, int desiredCount)
        {
            int row = index / FormationDepthColumns;
            int depth = index % FormationDepthColumns;
            int totalRows = Math.Max(1, (desiredCount + FormationDepthColumns - 1) / FormationDepthColumns);
            int centeredRowOffsetCm = (row * FormationRowSpacingCm) - (((totalRows - 1) * FormationRowSpacingCm) / 2);
            int staggerCm = ((depth & 1) == 0 ? -FormationDepthStaggerCm : FormationDepthStaggerCm) / 2;
            int y = TeamCenterY + centeredRowOffsetCm + staggerCm;
            int xBase = teamA ? TeamALeftX : TeamBRightX;
            int roleOffset = role switch
            {
                StressRole.Warrior => WarriorOffsetCm,
                StressRole.FireMage => FireMageOffsetCm,
                StressRole.LaserMage => LaserMageOffsetCm,
                _ => PriestOffsetCm,
            };

            int direction = teamA ? 1 : -1;
            int x = xBase + direction * (roleOffset + (depth * FormationDepthSpacingCm));
            return Fix64Vec2.FromInt(x, y);
        }

        private enum StressRole
        {
            Warrior,
            FireMage,
            LaserMage,
            Priest,
        }
    }
}
