import fs from "node:fs";
import path from "node:path";

const repoRoot = process.cwd();
const modRoot = path.join(repoRoot, "mods", "showcases", "rts_cnc_full", "RtsCncFullShowcaseMod");
const assetsRoot = path.join(modRoot, "assets");

const factions = [
  { key: "atlantic", display: "Atlantic Directorate", teamId: 1, color: "#3B82F6", origin: [9000, 10000] },
  { key: "volkov", display: "Volkov Union", teamId: 2, color: "#EF4444", origin: [19000, 9500] },
  { key: "nile", display: "Nile Compact", teamId: 3, color: "#F59E0B", origin: [8500, 20500] },
  { key: "pacific", display: "Pacific Combine", teamId: 4, color: "#10B981", origin: [19800, 21000] },
  { key: "andes", display: "Andes League", teamId: 5, color: "#A855F7", origin: [14000, 15500] },
];

const categories = [
  {
    key: "infantry",
    producer: "Barracks",
    short: "INF",
    health: 220,
    damage: 24,
    range: 420,
    speed: 620,
    units: ["Rifle Section", "Grenadier Team", "Combat Engineer", "Commando Cell"],
  },
  {
    key: "armor",
    producer: "Motor Pool",
    short: "ARM",
    health: 520,
    damage: 64,
    range: 560,
    speed: 520,
    units: ["Scout Rover", "Bulldog IFV", "Battle Tank", "Siege Crawler"],
  },
  {
    key: "air",
    producer: "Airfield",
    short: "AIR",
    health: 360,
    damage: 78,
    range: 760,
    speed: 840,
    units: ["Recon Drone", "Gunship", "Strike Bomber", "Airborne HQ"],
  },
  {
    key: "naval",
    producer: "Shipyard",
    short: "SEA",
    health: 640,
    damage: 82,
    range: 820,
    speed: 460,
    units: ["Patrol Boat", "Missile Frigate", "Submersible", "Carrier Tender"],
  },
  {
    key: "support",
    producer: "Command Lab",
    short: "SUP",
    health: 420,
    damage: 48,
    range: 900,
    speed: 500,
    units: ["Mobile Radar", "Repair Rig", "Artillery Battery", "Superweapon Team"],
  },
];

const producerOffsets = [
  [-900, -520],
  [0, -760],
  [900, -520],
  [-460, 460],
  [460, 460],
];

const unitStartOffset = [-1700, 1340];
const unitStep = [560, 430];

function ensureDir(relativePath) {
  fs.mkdirSync(path.join(assetsRoot, relativePath), { recursive: true });
}

function writeJson(relativePath, value) {
  const fullPath = path.join(assetsRoot, relativePath);
  fs.mkdirSync(path.dirname(fullPath), { recursive: true });
  fs.writeFileSync(fullPath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function slug(value) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "");
}

function pascal(value) {
  return value
    .split(/[^a-zA-Z0-9]+/g)
    .filter(Boolean)
    .map((part) => `${part[0].toUpperCase()}${part.slice(1)}`)
    .join("");
}

function templateId(faction, category, unitName) {
  return `rts_cnc_full_${faction.key}_${category.key}_${slug(unitName)}`;
}

function producerTemplateId(faction, category) {
  return `rts_cnc_full_${faction.key}_${category.key}_producer`;
}

function trainAbilityId(faction, category, unitName) {
  return `Ability.Rts.CncFull.Train.${pascal(faction.key)}.${pascal(category.key)}.${pascal(unitName)}`;
}

function trainEffectId(faction, category, unitName) {
  return `Effect.Rts.CncFull.Train.${pascal(faction.key)}.${pascal(category.key)}.${pascal(unitName)}`;
}

function baseUnitComponents(name, faction, category, unitIndex) {
  const health = category.health + (unitIndex * 42) + (faction.teamId * 7);
  const damage = category.damage + (unitIndex * 8) + faction.teamId;
  const range = category.range + (unitIndex * 45);
  const moveSpeed = category.speed - (unitIndex * 18);
  return {
    Name: { Value: `${faction.display} ${name}` },
    Team: { Id: faction.teamId },
    PlayerOwner: { PlayerId: faction.teamId },
    SelectionSelectableTag: {},
    SelectionSelectableState: { IsEnabled: true },
    WorldPositionCm: { Value: { X: 0, Y: 0 } },
    FacingDirection: { AngleRad: 0 },
    SpatialBounds: { kind: "Box3D", localCenterXCm: 0, localCenterYCm: 70, localCenterZCm: 0 },
    SpatialBox3D: { halfSizeXCm: 70, halfSizeYCm: 80, halfSizeZCm: 70 },
    AttributeBuffer: {
      base: { Health: health, Damage: damage, Range: range, MoveSpeed: moveSpeed },
      current: { Health: health, Damage: damage, Range: range, MoveSpeed: moveSpeed },
    },
    AbilityStateBuffer: {
      abilityIds: ["Ability.Rts.CncFull.Hold"],
    },
    GameplayTagContainer: {},
    TagCountContainer: {},
    TimedTagBuffer: {},
    OrderBuffer: {},
    BlackboardSpatialBuffer: {},
    BlackboardEntityBuffer: {},
    BlackboardIntBuffer: {},
  };
}

function producerComponents(faction, category) {
  const abilityIds = category.units.map((unitName) => trainAbilityId(faction, category, unitName));
  return {
    Name: { Value: `${faction.display} ${category.producer}` },
    Team: { Id: faction.teamId },
    PlayerOwner: { PlayerId: faction.teamId },
    SelectionSelectableTag: {},
    SelectionSelectableState: { IsEnabled: true },
    WorldPositionCm: { Value: { X: 0, Y: 0 } },
    FacingDirection: { AngleRad: 0 },
    SpatialBounds: { kind: "Box3D", localCenterXCm: 0, localCenterYCm: 150, localCenterZCm: 0 },
    SpatialBox3D: { halfSizeXCm: 190, halfSizeYCm: 170, halfSizeZCm: 190 },
    AttributeBuffer: {
      base: { Health: 1800, Credits: 25000, Power: 220, Ore: 2400 },
      current: { Health: 1800, Credits: 25000, Power: 220, Ore: 2400 },
    },
    AbilityStateBuffer: { abilityIds },
    GameplayTagContainer: {},
    TagCountContainer: {},
    TimedTagBuffer: {},
    OrderBuffer: {},
    BlackboardSpatialBuffer: {},
    BlackboardEntityBuffer: {},
    BlackboardIntBuffer: {},
  };
}

const templates = [
  {
    id: "rts_cnc_full_team_anchor",
    components: {
      Team: { Id: 1 },
      WorldPositionCm: { Value: { X: 0, Y: 0 } },
      AttributeBuffer: {
        base: { Credits: 0, Power: 0, Ore: 0 },
        current: { Credits: 0, Power: 0, Ore: 0 },
      },
    },
  },
  {
    id: "rts_cnc_full_player_anchor",
    components: {
      Team: { Id: 1 },
      PlayerOwner: { PlayerId: 1 },
      WorldPositionCm: { Value: { X: 0, Y: 0 } },
      AttributeBuffer: {
        base: { Credits: 0, Power: 0, Ore: 0 },
        current: { Credits: 0, Power: 0, Ore: 0 },
      },
    },
  },
];

for (const faction of factions) {
  for (const category of categories) {
    templates.push({
      id: producerTemplateId(faction, category),
      components: producerComponents(faction, category),
    });

    category.units.forEach((unitName, unitIndex) => {
      templates.push({
        id: templateId(faction, category, unitName),
        components: baseUnitComponents(unitName, faction, category, unitIndex),
      });
    });
  }
}

const abilities = [
  {
    id: "Ability.Rts.CncFull.Hold",
    exec: {
      clockId: "FixedFrame",
      items: [{ kind: "End", tick: 0 }],
    },
    input: { castModeOverride: "SmartCast" },
    presentation: {
      displayName: "Hold",
      iconGlyph: "H",
      accentColor: "#6B7280",
      hintText: "Idle tactical command slot.",
    },
  },
];

const effects = [
  {
    id: "Effect.Rts.CncFull.CostTrainingStep",
    tags: ["Effect.Rts.CncFull.Cost"],
    presetType: "InstantDamage",
    lifetime: "Instant",
    participatesInResponse: false,
    modifiers: [{ attribute: "Credits", op: "Add", value: -50 }],
  },
];

for (const faction of factions) {
  for (const category of categories) {
    category.units.forEach((unitName, unitIndex) => {
      const duration = 48 + (unitIndex * 18) + (faction.teamId * 3);
      abilities.push({
        id: trainAbilityId(faction, category, unitName),
        exec: {
          clockId: "FixedFrame",
          items: [
            { kind: "TagClip", tick: 0, duration, tag: "Status.Rts.Training" },
            { kind: "EffectSignal", tick: 0, template: "Effect.Rts.CncFull.CostTrainingStep", dispatchTarget: "Source" },
            { kind: "EffectSignal", tick: duration, template: trainEffectId(faction, category, unitName), dispatchTarget: "Source" },
            { kind: "End", tick: duration },
          ],
        },
        input: { castModeOverride: "SmartCast" },
        presentation: {
          displayName: `Train ${unitName}`,
          iconGlyph: `${category.short}${unitIndex + 1}`,
          accentColor: faction.color,
          hintText: `${faction.display} ${category.key} production.`,
        },
      });

      effects.push({
        id: trainEffectId(faction, category, unitName),
        tags: ["Effect.Rts.CncFull.Train"],
        presetType: "CreateUnit",
        lifetime: "Instant",
        participatesInResponse: false,
        unitCreation: {
          templateId: templateId(faction, category, unitName),
          placementPattern: "Scatter",
          count: 1,
          offsetRadius: category.key === "air" ? 520 : 360,
          copySourcePlayerOwner: true,
        },
      });
    });
  }
}

const mapEntities = [];
const teams = [];
const players = [];

for (const faction of factions) {
  const [originX, originY] = faction.origin;
  mapEntities.push({
    Template: "rts_cnc_full_team_anchor",
    InstanceId: `${faction.key}_team_anchor`,
    Overrides: {
      Team: { Id: faction.teamId },
      WorldPositionCm: { Value: { X: originX, Y: originY } },
    },
  });
  mapEntities.push({
    Template: "rts_cnc_full_player_anchor",
    InstanceId: `${faction.key}_player_anchor`,
    Overrides: {
      Team: { Id: faction.teamId },
      PlayerOwner: { PlayerId: faction.teamId },
      WorldPositionCm: { Value: { X: originX + 120, Y: originY + 120 } },
    },
  });
  teams.push({ TeamId: faction.teamId, RepresentativeInstanceId: `${faction.key}_team_anchor` });
  players.push({ PlayerId: faction.teamId, TeamId: faction.teamId, RepresentativeInstanceId: `${faction.key}_player_anchor` });

  categories.forEach((category, categoryIndex) => {
    const [dx, dy] = producerOffsets[categoryIndex];
    mapEntities.push({
      Template: producerTemplateId(faction, category),
      InstanceId: `${faction.key}_${category.key}_producer`,
      Overrides: {
        WorldPositionCm: { Value: { X: originX + dx, Y: originY + dy } },
        FacingDirection: { AngleRad: (faction.teamId - 1) * 0.63 },
      },
    });

    category.units.forEach((unitName, unitIndex) => {
      const rosterIndex = (categoryIndex * 4) + unitIndex;
      const col = rosterIndex % 5;
      const row = Math.floor(rosterIndex / 5);
      mapEntities.push({
        Template: templateId(faction, category, unitName),
        InstanceId: `${faction.key}_${category.key}_${slug(unitName)}`,
        Overrides: {
          WorldPositionCm: {
            Value: {
              X: originX + unitStartOffset[0] + (col * unitStep[0]),
              Y: originY + unitStartOffset[1] + (row * unitStep[1]),
            },
          },
          FacingDirection: { AngleRad: (faction.teamId - 1) * 0.48 },
        },
      });
    });
  });
}

const map = {
  Id: "rts_cnc_full",
  Tags: ["rts", "rts_showcase", "rts_production", "rts_cnc_full", "cnc"],
  DefaultCamera: {
    VirtualCameraId: "Rts",
    TargetXCm: 14000,
    TargetYCm: 15500,
    Yaw: 195,
    Pitch: 54,
    DistanceCm: 15800,
    FovYDeg: 60,
  },
  Boards: [
    {
      Name: "default",
      SpatialType: "Grid",
      widthInMacroTiles: 96,
      heightInMacroTiles: 96,
      GridCellSizeCm: 400,
      ChunkSizeCells: 32,
      NavigationEnabled: false,
    },
  ],
  Entities: mapEntities,
  Teams: teams,
  Players: players,
};

const graphs = [
  {
    id: "Graph.RtsCncFull.RosterTotals",
    kind: "Query",
    entry: "unitTypes",
    nodes: [
      { id: "unitTypes", op: "ConstInt", intValue: 100, next: "factions" },
      { id: "factions", op: "ConstInt", intValue: 5, next: "producers" },
      { id: "producers", op: "ConstInt", intValue: 25 },
    ],
    outputs: [
      {
        id: "unitTypes",
        destination: "Summary",
        type: "Int",
        source: "unitTypes",
        key: "rts.cnc.full.unitTypes",
        title: "Unit Types",
        summary: "Five factions x twenty trainable unit types.",
      },
      {
        id: "factions",
        destination: "Summary",
        type: "Int",
        source: "factions",
        key: "rts.cnc.full.factions",
        title: "Factions",
        summary: "Playable countries in the C&C full showcase.",
      },
      {
        id: "producers",
        destination: "Summary",
        type: "Int",
        source: "producers",
        key: "rts.cnc.full.producers",
        title: "Production Buildings",
        summary: "Five producer categories per faction.",
      },
    ],
  },
];

const itemShapes = [
  { id: "rts_cnc_full_1x1", rows: ["#"], rotatable: false },
  { id: "rts_cnc_full_2x1", rows: ["##"], rotatable: true },
  { id: "rts_cnc_full_2x2", rows: ["##", "##"], rotatable: false },
];

const itemLayouts = [
  {
    id: "rts_cnc_full_supply_pallet",
    purpose: "Stash",
    width: 8,
    height: 4,
    namedSlots: [
      { id: "munitions", label: "Munitions", requiredAll: ["Item.RtsCncFull.Munitions"], singleItemOnly: false },
      { id: "power", label: "Power", requiredAll: ["Item.RtsCncFull.Power"], singleItemOnly: false },
      { id: "parts", label: "Parts", requiredAll: ["Item.RtsCncFull.Parts"], singleItemOnly: false },
    ],
  },
];

const itemDefinitions = [
  {
    id: "rts_cnc_full_ore_canister",
    displayName: "Ore Canister",
    shape: "rts_cnc_full_1x1",
    maxStack: 999,
    tags: ["Item.RtsCncFull.Munitions"],
    allowedNamedSlots: ["munitions"],
  },
  {
    id: "rts_cnc_full_power_core",
    displayName: "Power Core",
    shape: "rts_cnc_full_1x1",
    maxStack: 99,
    tags: ["Item.RtsCncFull.Power"],
    allowedNamedSlots: ["power"],
  },
  {
    id: "rts_cnc_full_vehicle_kit",
    displayName: "Vehicle Kit",
    shape: "rts_cnc_full_2x1",
    maxStack: 20,
    tags: ["Item.RtsCncFull.Parts"],
    allowedNamedSlots: ["parts"],
  },
  {
    id: "rts_cnc_full_airframe_crate",
    displayName: "Airframe Crate",
    shape: "rts_cnc_full_2x2",
    maxStack: 10,
    tags: ["Item.RtsCncFull.Parts"],
    allowedNamedSlots: ["parts"],
  },
  {
    id: "rts_cnc_full_naval_parts",
    displayName: "Naval Parts",
    shape: "rts_cnc_full_2x2",
    maxStack: 10,
    tags: ["Item.RtsCncFull.Parts"],
    allowedNamedSlots: ["parts"],
  },
];

ensureDir("Entities");
ensureDir("GAS");
ensureDir("Maps");
ensureDir("Items");

writeJson("Entities/templates.json", templates);
writeJson("GAS/abilities.json", abilities);
writeJson("GAS/effects.json", effects);
writeJson("GAS/graphs.json", graphs);
writeJson("Maps/rts_cnc_full.json", map);
writeJson("Items/shapes.json", itemShapes);
writeJson("Items/layouts.json", itemLayouts);
writeJson("Items/definitions.json", itemDefinitions);
writeJson("game.json", {
  startupMapId: "rts_cnc_full",
  startupSelectedPlayerId: 1,
  windowTitle: "Ludots Engine - C&C Full RTS Showcase",
  windowWidth: 1440,
  windowHeight: 900,
  windowResizable: true,
  targetFps: 60,
});

fs.writeFileSync(
  path.join(assetsRoot, "CC0_NOTICE.md"),
  [
    "# CC0 Notice",
    "",
    "The RtsCncFullShowcaseMod-authored roster names, JSON configs, generated gameplay data, and documentation in this asset folder are dedicated to the public domain via CC0 1.0.",
    "",
    "No external third-party art pack is bundled. Runtime visuals use Ludots built-in primitive renderer assets and mod-authored data only.",
    "",
  ].join("\n"),
  "utf8",
);

console.log(`Generated C&C full showcase assets in ${assetsRoot}`);
console.log(`Templates: ${templates.length}; abilities: ${abilities.length}; effects: ${effects.length}; map entities: ${mapEntities.length}`);
