import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const modRoot = path.join(repoRoot, 'mods', 'showcases', 'rts_starcraft_like', 'RtsStarCraftLikeShowcaseMod');
const assetsRoot = path.join(modRoot, 'assets');

const holdAbility = 'Ability.Rts.StarCraft.Shared.Hold';

const races = [
  {
    id: 'terran',
    teamId: 1,
    label: 'Terran Dominion',
    color: '#59A7FF',
    origin: { x: 5200, y: 5600 },
    resources: { Minerals: 5600, Gas: 1800, Supply: 90, Energy: 150 },
    units: [
      ['command_center', 'Terran Command Center', 'structure', ['Ability.Rts.StarCraft.Terran.TrainMarine', 'Ability.Rts.StarCraft.Terran.TrainSiegeTank', 'Ability.Rts.StarCraft.Terran.CalldownScan', 'Ability.Rts.StarCraft.Terran.CallMule']],
      ['scv', 'Terran SCV', 'worker'],
      ['marine', 'Terran Marine', 'infantry'],
      ['marauder', 'Terran Marauder', 'infantry'],
      ['reaper', 'Terran Reaper', 'infantry'],
      ['ghost', 'Terran Ghost', 'specialist'],
      ['firebat', 'Terran Firebat', 'infantry'],
      ['medic', 'Terran Medic', 'support'],
      ['hellion', 'Terran Hellion', 'vehicle'],
      ['hellbat', 'Terran Hellbat', 'vehicle'],
      ['widow_mine', 'Terran Widow Mine', 'vehicle'],
      ['siege_tank', 'Terran Siege Tank', 'vehicle'],
      ['cyclone', 'Terran Cyclone', 'vehicle'],
      ['thor', 'Terran Thor', 'vehicle'],
      ['viking', 'Terran Viking', 'air'],
      ['medivac', 'Terran Medivac', 'air'],
      ['liberator', 'Terran Liberator', 'air'],
      ['raven', 'Terran Raven', 'air'],
      ['banshee', 'Terran Banshee', 'air'],
      ['battlecruiser', 'Terran Battlecruiser', 'capital'],
      ['planetary_fortress', 'Terran Planetary Fortress', 'structure'],
      ['orbital_command', 'Terran Orbital Command', 'structure'],
      ['supply_depot', 'Terran Supply Depot', 'structure'],
      ['barracks', 'Terran Barracks', 'structure', ['Ability.Rts.StarCraft.Terran.TrainMarine', 'Ability.Rts.StarCraft.Terran.TrainMarauder', holdAbility, holdAbility]],
      ['engineering_bay', 'Terran Engineering Bay', 'structure'],
      ['bunker', 'Terran Bunker', 'structure'],
      ['refinery', 'Terran Refinery', 'structure'],
      ['factory', 'Terran Factory', 'structure', ['Ability.Rts.StarCraft.Terran.TrainHellion', 'Ability.Rts.StarCraft.Terran.TrainSiegeTank', holdAbility, holdAbility]],
      ['starport', 'Terran Starport', 'structure', ['Ability.Rts.StarCraft.Terran.TrainViking', 'Ability.Rts.StarCraft.Terran.TrainMedivac', holdAbility, holdAbility]],
      ['armory', 'Terran Armory', 'structure'],
      ['fusion_core', 'Terran Fusion Core', 'structure'],
      ['missile_turret', 'Terran Missile Turret', 'structure'],
      ['sensor_tower', 'Terran Sensor Tower', 'structure'],
      ['merc_compound', 'Terran Merc Compound', 'structure']
    ],
  },
  {
    id: 'zerg',
    teamId: 2,
    label: 'Zerg Swarm',
    color: '#A6E22E',
    origin: { x: 2850, y: 9000 },
    resources: { Minerals: 5000, Gas: 1500, Supply: 120, Biomass: 240 },
    units: [
      ['hatchery', 'Zerg Hatchery', 'structure', ['Ability.Rts.StarCraft.Zerg.SpawnZergling', 'Ability.Rts.StarCraft.Zerg.SpawnHydralisk', 'Ability.Rts.StarCraft.Zerg.CreepSurge', 'Ability.Rts.StarCraft.Zerg.MorphUltralisk']],
      ['drone', 'Zerg Drone', 'worker'],
      ['overlord', 'Zerg Overlord', 'air'],
      ['zergling', 'Zergling', 'infantry'],
      ['queen', 'Zerg Queen', 'support'],
      ['baneling', 'Baneling', 'infantry'],
      ['roach', 'Roach', 'infantry'],
      ['ravager', 'Ravager', 'infantry'],
      ['hydralisk', 'Hydralisk', 'infantry'],
      ['lurker', 'Lurker', 'specialist'],
      ['mutalisk', 'Mutalisk', 'air'],
      ['corruptor', 'Corruptor', 'air'],
      ['brood_lord', 'Brood Lord', 'capital'],
      ['viper', 'Viper', 'air'],
      ['infestor', 'Infestor', 'caster'],
      ['swarm_host', 'Swarm Host', 'specialist'],
      ['ultralisk', 'Ultralisk', 'heavy'],
      ['spine_crawler', 'Spine Crawler', 'structure'],
      ['spore_crawler', 'Spore Crawler', 'structure'],
      ['spawning_pool', 'Spawning Pool', 'structure'],
      ['extractor', 'Extractor', 'structure'],
      ['evolution_chamber', 'Evolution Chamber', 'structure'],
      ['roach_warren', 'Roach Warren', 'structure'],
      ['baneling_nest', 'Baneling Nest', 'structure'],
      ['lair', 'Lair', 'structure'],
      ['hive', 'Hive', 'structure'],
      ['hydralisk_den', 'Hydralisk Den', 'structure'],
      ['spire', 'Spire', 'structure'],
      ['greater_spire', 'Greater Spire', 'structure'],
      ['infestation_pit', 'Infestation Pit', 'structure'],
      ['ultralisk_cavern', 'Ultralisk Cavern', 'structure'],
      ['nydus_network', 'Nydus Network', 'structure'],
      ['creep_tumor', 'Creep Tumor', 'structure']
    ],
  },
  {
    id: 'protoss',
    teamId: 3,
    label: 'Protoss Conclave',
    color: '#B692FF',
    origin: { x: 9000, y: 7900 },
    resources: { Minerals: 5400, Gas: 2000, Supply: 100, Psi: 320 },
    units: [
      ['nexus', 'Protoss Nexus', 'structure', ['Ability.Rts.StarCraft.Protoss.WarpZealot', 'Ability.Rts.StarCraft.Protoss.WarpStalker', 'Ability.Rts.StarCraft.Protoss.ChronoBoost', 'Ability.Rts.StarCraft.Protoss.WarpDarkTemplar']],
      ['probe', 'Protoss Probe', 'worker'],
      ['zealot', 'Protoss Zealot', 'infantry'],
      ['stalker', 'Protoss Stalker', 'infantry'],
      ['sentry', 'Protoss Sentry', 'support'],
      ['adept', 'Protoss Adept', 'infantry'],
      ['high_templar', 'High Templar', 'caster'],
      ['dark_templar', 'Dark Templar', 'specialist'],
      ['archon', 'Archon', 'heavy'],
      ['immortal', 'Immortal', 'heavy'],
      ['colossus', 'Colossus', 'heavy'],
      ['disruptor', 'Disruptor', 'specialist'],
      ['observer', 'Observer', 'air'],
      ['warp_prism', 'Warp Prism', 'air'],
      ['phoenix', 'Phoenix', 'air'],
      ['void_ray', 'Void Ray', 'air'],
      ['oracle', 'Oracle', 'air'],
      ['tempest', 'Tempest', 'capital'],
      ['carrier', 'Carrier', 'capital'],
      ['mothership', 'Mothership', 'capital'],
      ['pylon', 'Pylon', 'structure'],
      ['assimilator', 'Assimilator', 'structure'],
      ['gateway', 'Protoss Gateway', 'structure', ['Ability.Rts.StarCraft.Protoss.WarpZealot', 'Ability.Rts.StarCraft.Protoss.WarpStalker', holdAbility, holdAbility]],
      ['forge', 'Protoss Forge', 'structure'],
      ['cybernetics_core', 'Cybernetics Core', 'structure'],
      ['shield_battery', 'Shield Battery', 'structure'],
      ['robotics_facility', 'Robotics Facility', 'structure', ['Ability.Rts.StarCraft.Protoss.WarpImmortal', holdAbility, holdAbility, holdAbility]],
      ['stargate', 'Stargate', 'structure', ['Ability.Rts.StarCraft.Protoss.WarpPhoenix', holdAbility, holdAbility, holdAbility]],
      ['twilight_council', 'Twilight Council', 'structure'],
      ['templar_archives', 'Templar Archives', 'structure'],
      ['dark_shrine', 'Dark Shrine', 'structure'],
      ['fleet_beacon', 'Fleet Beacon', 'structure'],
      ['robotics_bay', 'Robotics Bay', 'structure']
    ],
  },
];

const trainAbilities = [
  ['Terran.TrainMarine', 'Train Marine', 'TM', 'rts_sc_terran_marine', 40, { Minerals: -50 }],
  ['Terran.TrainMarauder', 'Train Marauder', 'MR', 'rts_sc_terran_marauder', 56, { Minerals: -100, Gas: -25 }],
  ['Terran.TrainHellion', 'Train Hellion', 'HL', 'rts_sc_terran_hellion', 58, { Minerals: -100 }],
  ['Terran.TrainSiegeTank', 'Train Siege Tank', 'ST', 'rts_sc_terran_siege_tank', 88, { Minerals: -150, Gas: -125 }],
  ['Terran.TrainViking', 'Train Viking', 'VK', 'rts_sc_terran_viking', 78, { Minerals: -150, Gas: -75 }],
  ['Terran.TrainMedivac', 'Train Medivac', 'MV', 'rts_sc_terran_medivac', 72, { Minerals: -100, Gas: -100 }],
  ['Zerg.SpawnZergling', 'Spawn Zergling', 'ZG', 'rts_sc_zerg_zergling', 28, { Minerals: -50 }],
  ['Zerg.SpawnHydralisk', 'Spawn Hydralisk', 'HY', 'rts_sc_zerg_hydralisk', 62, { Minerals: -100, Gas: -50 }],
  ['Zerg.MorphUltralisk', 'Morph Ultralisk', 'UL', 'rts_sc_zerg_ultralisk', 110, { Minerals: -300, Gas: -200 }],
  ['Protoss.WarpZealot', 'Warp Zealot', 'WZ', 'rts_sc_protoss_zealot', 52, { Minerals: -100 }],
  ['Protoss.WarpStalker', 'Warp Stalker', 'WS', 'rts_sc_protoss_stalker', 66, { Minerals: -125, Gas: -50 }],
  ['Protoss.WarpDarkTemplar', 'Warp Dark Templar', 'DT', 'rts_sc_protoss_dark_templar', 92, { Minerals: -125, Gas: -125 }],
  ['Protoss.WarpImmortal', 'Warp Immortal', 'IM', 'rts_sc_protoss_immortal', 88, { Minerals: -250, Gas: -100 }],
  ['Protoss.WarpPhoenix', 'Warp Phoenix', 'PX', 'rts_sc_protoss_phoenix', 70, { Minerals: -150, Gas: -100 }]
];

const graphAbilities = [
  ['Terran.CalldownScan', 'Scanner Sweep', 'SS', 'Effect.Rts.StarCraft.Terran.ScanPulse', 'Expose enemy movement and spend command energy.'],
  ['Terran.CallMule', 'Call MULE', 'MU', 'Effect.Rts.StarCraft.Terran.MuleDrop', 'Convert command energy into a visible minerals burst.'],
  ['Zerg.CreepSurge', 'Creep Surge', 'CS', 'Effect.Rts.StarCraft.Zerg.CreepSurge', 'Push biomass through the swarm economy graph.'],
  ['Protoss.ChronoBoost', 'Chrono Boost', 'CB', 'Effect.Rts.StarCraft.Protoss.ChronoBoost', 'Pulse psi through shields via GAS graph.']
];

const abilityAccent = {
  Terran: '#59A7FF',
  Zerg: '#A6E22E',
  Protoss: '#B692FF'
};

function ensureDir(relative) {
  fs.mkdirSync(path.join(assetsRoot, relative), { recursive: true });
}

function writeJson(relativePath, value) {
  const fullPath = path.join(assetsRoot, relativePath);
  fs.mkdirSync(path.dirname(fullPath), { recursive: true });
  fs.writeFileSync(fullPath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

function templateId(raceId, unitId) {
  return `rts_sc_${raceId}_${unitId}`;
}

function isStructure(kind) {
  return kind === 'structure';
}

function baseAttributes(race, kind, index) {
  const hpBase = isStructure(kind) ? 950 : kind === 'capital' ? 820 : kind === 'heavy' ? 620 : kind === 'worker' ? 220 : 360;
  const attrs = {
    Health: hpBase + (index % 7) * 35,
    MoveSpeed: isStructure(kind) ? 0 : 560 + (index % 4) * 20,
    Damage: isStructure(kind) ? 0 : 16 + (index % 8) * 4,
    Armor: isStructure(kind) ? 2 + (index % 3) : index % 3,
    Supply: isStructure(kind) ? 0 : 1 + (index % 4)
  };

  if (race.id === 'protoss') {
    attrs.Shield = Math.round(attrs.Health * 0.55);
    attrs.Psi = 40 + index * 2;
  }

  if (race.id === 'terran') {
    attrs.Energy = index === 0 ? 150 : 0;
  }

  if (race.id === 'zerg') {
    attrs.Biomass = 20 + index * 3;
  }

  if (index === 0) {
    Object.assign(attrs, race.resources);
  }

  return attrs;
}

function componentsFor(race, unit, index) {
  const [unitId, displayName, kind, abilities] = unit;
  const attrs = baseAttributes(race, kind, index);
  const components = {
    Name: { Value: displayName },
    Team: { Id: race.teamId },
    PlayerOwner: { PlayerId: 1 },
    SelectionSelectableTag: {},
    SelectionSelectableState: { IsEnabled: true },
    WorldPositionCm: { Value: { X: 0, Y: 0 } },
    FacingDirection: { AngleRad: 0 },
    AttributeBuffer: { base: attrs, current: attrs },
    AbilityStateBuffer: {
      abilityIds: abilities ?? [holdAbility]
    },
    GameplayTagContainer: {},
    TagCountContainer: {},
    TimedTagBuffer: {},
    OrderBuffer: {},
    BlackboardSpatialBuffer: {},
    BlackboardEntityBuffer: {},
    BlackboardIntBuffer: {}
  };

  if (isStructure(kind)) {
    components.PresentationStaticTransform = {};
    components.SpatialBounds = { kind: 'Box3D', localCenterXCm: 0, localCenterYCm: 145, localCenterZCm: 0 };
    components.SpatialBox3D = { halfSizeXCm: 190, halfSizeYCm: 150, halfSizeZCm: 170 };
  }

  if (unitId === 'command_center') {
    components.AbilityFormSetRef = { formSetId: 'rts_sc_terran_command_forms' };
  } else if (unitId === 'hatchery') {
    components.AbilityFormSetRef = { formSetId: 'rts_sc_zerg_hatchery_forms' };
  } else if (unitId === 'nexus') {
    components.AbilityFormSetRef = { formSetId: 'rts_sc_protoss_nexus_forms' };
  }

  return components;
}

function buildTemplates() {
  return races.flatMap((race) => race.units.map((unit, index) => ({
    id: templateId(race.id, unit[0]),
    components: componentsFor(race, unit, index)
  })));
}

function buildMapEntities() {
  const entities = [];
  for (const race of races) {
    race.units.forEach((unit, index) => {
      const col = index % 7;
      const row = Math.floor(index / 7);
      const kind = unit[2];
      const spacingX = isStructure(kind) ? 460 : 310;
      const spacingY = isStructure(kind) ? 420 : 285;
      entities.push({
        Template: templateId(race.id, unit[0]),
        InstanceId: `${race.id}_${unit[0]}`,
        Overrides: {
          WorldPositionCm: {
            Value: {
              X: race.origin.x + col * spacingX,
              Y: race.origin.y + row * spacingY
            }
          },
          Name: { Value: unit[1] },
          Team: { Id: race.teamId },
          PlayerOwner: { PlayerId: 1 }
        }
      });
    });
  }

  return entities;
}

function buildAbilities() {
  const abilities = [
    {
      id: holdAbility,
      exec: { clockId: 'FixedFrame', items: [{ kind: 'End', tick: 0 }] },
      presentation: {
        displayName: 'Hold',
        iconGlyph: 'H',
        accentColor: '#6B7280',
        hintText: 'Keep the selected squad ready.'
      }
    }
  ];

  for (const [key, displayName, glyph, targetTemplate, duration, cost] of trainAbilities) {
    const raceName = key.split('.')[0];
    abilities.push({
      id: `Ability.Rts.StarCraft.${key}`,
      exec: {
        clockId: 'FixedFrame',
        items: [
          { kind: 'TagClip', tick: 0, duration, tag: 'Status.Rts.Training' },
          { kind: 'EffectSignal', tick: 0, template: `Effect.Rts.StarCraft.${key}.Cost` },
          { kind: 'EffectSignal', tick: duration, template: `Effect.Rts.StarCraft.${key}`, dispatchTarget: 'Source' },
          { kind: 'End', tick: duration }
        ]
      },
      blockTags: { blockedAny: ['Status.Rts.Training', 'State.Rts.Constructing'] },
      presentation: {
        displayName,
        iconGlyph: glyph,
        accentColor: abilityAccent[raceName] ?? '#59A7FF',
        hintText: `Create ${targetTemplate.replace('rts_sc_', '').replaceAll('_', ' ')} through the GAS production queue.`
      }
    });
  }

  for (const [key, displayName, glyph, effect, hintText] of graphAbilities) {
    const raceName = key.split('.')[0];
    abilities.push({
      id: `Ability.Rts.StarCraft.${key}`,
      exec: {
        clockId: 'FixedFrame',
        items: [
          { kind: 'EffectSignal', tick: 0, template: effect },
          { kind: 'End', tick: 0 }
        ]
      },
      presentation: {
        displayName,
        iconGlyph: glyph,
        accentColor: abilityAccent[raceName] ?? '#59A7FF',
        hintText
      }
    });
  }

  return abilities;
}

function modifierRows(cost) {
  return Object.entries(cost).map(([attribute, value]) => ({ attribute, op: 'Add', value }));
}

function buildEffects() {
  const effects = [];
  for (const [key, , , targetTemplate, , cost] of trainAbilities) {
    effects.push({
      id: `Effect.Rts.StarCraft.${key}.Cost`,
      tags: ['Effect.Rts.StarCraft.Cost'],
      presetType: 'InstantDamage',
      lifetime: 'Instant',
      participatesInResponse: false,
      modifiers: modifierRows(cost)
    });
    effects.push({
      id: `Effect.Rts.StarCraft.${key}`,
      tags: ['Effect.Rts.StarCraft.Production'],
      presetType: 'CreateUnit',
      lifetime: 'Instant',
      participatesInResponse: false,
      unitCreation: {
        templateId: targetTemplate,
        placementPattern: 'Scatter',
        count: 1,
        offsetRadius: 340,
        onSpawnEffect: 'Effect.Rts.Shared.ApplySpawnTargetOrder',
        copySourcePlayerOwner: true
      }
    });
  }

  effects.push(
    {
      id: 'Effect.Rts.StarCraft.Terran.ScanPulse',
      tags: ['Effect.Rts.StarCraft.Terran.Graph'],
      presetType: 'Buff',
      lifetime: 'After',
      participatesInResponse: false,
      duration: { durationTicks: 1, periodTicks: 0, clockId: 'FixedFrame' },
      phaseGraphs: { OnApply: { post: 'Graph.Rts.StarCraft.Terran.ScanPulse' } }
    },
    {
      id: 'Effect.Rts.StarCraft.Terran.MuleDrop',
      tags: ['Effect.Rts.StarCraft.Terran.Graph'],
      presetType: 'Buff',
      lifetime: 'After',
      participatesInResponse: false,
      duration: { durationTicks: 1, periodTicks: 0, clockId: 'FixedFrame' },
      phaseGraphs: { OnApply: { post: 'Graph.Rts.StarCraft.Terran.MuleDrop' } }
    },
    {
      id: 'Effect.Rts.StarCraft.Zerg.CreepSurge',
      tags: ['Effect.Rts.StarCraft.Zerg.Graph'],
      presetType: 'Buff',
      lifetime: 'After',
      participatesInResponse: false,
      duration: { durationTicks: 1, periodTicks: 0, clockId: 'FixedFrame' },
      phaseGraphs: { OnApply: { post: 'Graph.Rts.StarCraft.Zerg.CreepSurge' } }
    },
    {
      id: 'Effect.Rts.StarCraft.Protoss.ChronoBoost',
      tags: ['Effect.Rts.StarCraft.Protoss.Graph'],
      presetType: 'Buff',
      lifetime: 'After',
      participatesInResponse: false,
      duration: { durationTicks: 1, periodTicks: 0, clockId: 'FixedFrame' },
      phaseGraphs: { OnApply: { post: 'Graph.Rts.StarCraft.Protoss.ChronoBoost' } }
    },
    {
      id: 'Effect.Rts.StarCraft.Item.TerranStimpack',
      tags: ['Effect.Rts.StarCraft.Item'],
      presetType: 'Buff',
      lifetime: 'Infinite',
      participatesInResponse: false,
      modifiers: [{ attribute: 'MoveSpeed', op: 'Add', value: 80 }]
    },
    {
      id: 'Effect.Rts.StarCraft.Item.ZergAdrenal',
      tags: ['Effect.Rts.StarCraft.Item'],
      presetType: 'Buff',
      lifetime: 'Infinite',
      participatesInResponse: false,
      modifiers: [{ attribute: 'Damage', op: 'Add', value: 6 }]
    },
    {
      id: 'Effect.Rts.StarCraft.Item.ProtossPsiMatrix',
      tags: ['Effect.Rts.StarCraft.Item'],
      presetType: 'Buff',
      lifetime: 'Infinite',
      participatesInResponse: false,
      modifiers: [{ attribute: 'Shield', op: 'Add', value: 120 }]
    }
  );

  return effects;
}

function graph(id, attribute, delta) {
  return {
    id,
    kind: 'Effect',
    entry: 'target',
    nodes: [
      { id: 'target', op: 'LoadContextTarget', next: 'delta' },
      { id: 'delta', op: 'ConstFloat', floatValue: delta, next: 'modify' },
      { id: 'modify', op: 'ModifyAttributeAdd', attribute, inputs: ['target', 'delta'] }
    ]
  };
}

function buildGraphs() {
  return [
    graph('Graph.Rts.StarCraft.Terran.ScanPulse', 'Energy', -25),
    graph('Graph.Rts.StarCraft.Terran.MuleDrop', 'Minerals', 75),
    graph('Graph.Rts.StarCraft.Zerg.CreepSurge', 'Biomass', 50),
    graph('Graph.Rts.StarCraft.Protoss.ChronoBoost', 'Shield', 75)
  ];
}

function buildAttributeConstraints() {
  return {
    Energy: { clampToBase: true, min: 0 },
    Biomass: { min: 0 },
    Shield: { min: 0 },
    Psi: { min: 0 },
    Supply: { min: 0 },
    Damage: { min: 0 },
    Armor: { min: 0 },
    MoveSpeed: { min: 0 }
  };
}

function buildFormSets() {
  return [
    {
      id: 'rts_sc_terran_command_forms',
      routes: [{ priority: 10, slotOverrides: [{ slotIndex: 3, abilityId: 'Ability.Rts.StarCraft.Terran.CallMule' }] }]
    },
    {
      id: 'rts_sc_zerg_hatchery_forms',
      routes: [{ priority: 10, slotOverrides: [{ slotIndex: 3, abilityId: 'Ability.Rts.StarCraft.Zerg.MorphUltralisk' }] }]
    },
    {
      id: 'rts_sc_protoss_nexus_forms',
      routes: [{ priority: 10, slotOverrides: [{ slotIndex: 3, abilityId: 'Ability.Rts.StarCraft.Protoss.WarpDarkTemplar' }] }]
    }
  ];
}

function buildItems() {
  return {
    shapes: [
      { id: 'rts_sc_chip', rows: ['1'], rotatable: false },
      { id: 'rts_sc_module', rows: ['11'], rotatable: true }
    ],
    layouts: [
      {
        id: 'rts_sc_armory_loadout',
        purpose: 'Equipment',
        width: 6,
        height: 2,
        grantsEquipmentBonuses: true,
        namedSlots: [
          { id: 'doctrine', label: 'Doctrine', requiredAll: ['Item.Rts.StarCraft.Doctrine'], singleItemOnly: true },
          { id: 'weapon', label: 'Weapon Protocol', requiredAll: ['Item.Rts.StarCraft.Weapon'], singleItemOnly: true },
          { id: 'tech', label: 'Tech Matrix', requiredAll: ['Item.Rts.StarCraft.Tech'], singleItemOnly: true }
        ]
      }
    ],
    definitions: [
      {
        id: 'rts_sc_item_terran_stimpack',
        displayName: 'Terran Stimpack Doctrine',
        shape: 'rts_sc_chip',
        tags: ['Item.Rts.StarCraft.Doctrine', 'Item.Rts.StarCraft.Terran'],
        allowedNamedSlots: ['doctrine'],
        equipEffects: ['Effect.Rts.StarCraft.Item.TerranStimpack'],
        abilityGrants: [{ slotIndex: 4, ability: 'Ability.Rts.StarCraft.Terran.CalldownScan' }]
      },
      {
        id: 'rts_sc_item_zerg_adrenal_glands',
        displayName: 'Zerg Adrenal Glands',
        shape: 'rts_sc_chip',
        tags: ['Item.Rts.StarCraft.Weapon', 'Item.Rts.StarCraft.Zerg'],
        allowedNamedSlots: ['weapon'],
        equipEffects: ['Effect.Rts.StarCraft.Item.ZergAdrenal'],
        abilityGrants: [{ slotIndex: 4, ability: 'Ability.Rts.StarCraft.Zerg.CreepSurge' }]
      },
      {
        id: 'rts_sc_item_protoss_psi_matrix',
        displayName: 'Protoss Psi Matrix',
        shape: 'rts_sc_module',
        tags: ['Item.Rts.StarCraft.Tech', 'Item.Rts.StarCraft.Protoss'],
        allowedNamedSlots: ['tech'],
        equipEffects: ['Effect.Rts.StarCraft.Item.ProtossPsiMatrix'],
        abilityGrants: [{ slotIndex: 4, ability: 'Ability.Rts.StarCraft.Protoss.ChronoBoost' }]
      }
    ]
  };
}

function buildRoster() {
  return {
    id: 'rts_starcraft_like_roster',
    races: races.map((race) => ({
      id: race.id,
      label: race.label,
      teamId: race.teamId,
      color: race.color,
      unitCount: race.units.length,
      units: race.units.map((unit) => ({
        id: templateId(race.id, unit[0]),
        label: unit[1],
        role: unit[2]
      }))
    })),
    totalUnitTypes: races.reduce((sum, race) => sum + race.units.length, 0)
  };
}

function main() {
  for (const dir of ['Entities', 'Maps', 'GAS', 'Items', 'StarCraftLike']) {
    ensureDir(dir);
  }

  const templates = buildTemplates();
  const items = buildItems();
  writeJson('Entities/templates.json', templates);
  writeJson('Maps/rts_starcraft_like.json', {
    Id: 'rts_starcraft_like',
    Tags: ['rts', 'rts_showcase', 'rts_production', 'starcraft_like', 'sc2'],
    DefaultCamera: {
      VirtualCameraId: 'Rts',
      TargetXCm: 6800,
      TargetYCm: 7600,
      Yaw: 180,
      Pitch: 48,
      DistanceCm: 11800,
      FovYDeg: 60
    },
    Boards: [
      { Name: 'default', SpatialType: 'HexGrid', DataFile: 'sc2_highlands.vtxm' }
    ],
    Entities: buildMapEntities()
  });
  writeJson('GAS/abilities.json', buildAbilities());
  writeJson('GAS/effects.json', buildEffects());
  writeJson('GAS/graphs.json', buildGraphs());
  writeJson('GAS/attribute_constraints.json', buildAttributeConstraints());
  writeJson('GAS/ability_form_sets.json', buildFormSets());
  writeJson('Items/shapes.json', items.shapes);
  writeJson('Items/layouts.json', items.layouts);
  writeJson('Items/definitions.json', items.definitions);
  writeJson('StarCraftLike/roster.json', buildRoster());
}

main();
