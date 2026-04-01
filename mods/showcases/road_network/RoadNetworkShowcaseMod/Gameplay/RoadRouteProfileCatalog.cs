using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal enum RoadRouteCorridorPreference : byte
    {
        Direct = 0,
        PreferNorth = 1,
        PreferSouth = 2,
    }

    internal readonly struct RoadRoutePlannerProfile
    {
        public readonly byte PresetId;
        public readonly string Label;
        public readonly string AgentTypeId;
        public readonly RoadRouteCorridorPreference CorridorPreference;
        public readonly float DirectBiasCm;
        public readonly float NorthBiasCm;
        public readonly float SouthBiasCm;
        public readonly bool AllowNorthVariant;
        public readonly bool AllowSouthVariant;

        public RoadRoutePlannerProfile(
            byte presetId,
            string label,
            string agentTypeId,
            RoadRouteCorridorPreference corridorPreference,
            float directBiasCm,
            float northBiasCm,
            float southBiasCm,
            bool allowNorthVariant,
            bool allowSouthVariant)
        {
            PresetId = presetId;
            Label = label;
            AgentTypeId = agentTypeId;
            CorridorPreference = corridorPreference;
            DirectBiasCm = directBiasCm;
            NorthBiasCm = northBiasCm;
            SouthBiasCm = southBiasCm;
            AllowNorthVariant = allowNorthVariant;
            AllowSouthVariant = allowSouthVariant;
        }
    }

    internal readonly struct RoadRouteExecutionProfile
    {
        public readonly byte PresetId;
        public readonly string Label;
        public readonly float WaypointRadiusCm;
        public readonly float FinalArrivalRadiusCm;
        public readonly float SpeedMultiplier;
        public readonly float MinProgressCm;
        public readonly float StallTimeoutSeconds;
        public readonly short MaxTimeoutRecoveries;

        public RoadRouteExecutionProfile(
            byte presetId,
            string label,
            float waypointRadiusCm,
            float finalArrivalRadiusCm,
            float speedMultiplier,
            float minProgressCm,
            float stallTimeoutSeconds,
            short maxTimeoutRecoveries)
        {
            PresetId = presetId;
            Label = label;
            WaypointRadiusCm = waypointRadiusCm;
            FinalArrivalRadiusCm = finalArrivalRadiusCm;
            SpeedMultiplier = speedMultiplier;
            MinProgressCm = minProgressCm;
            StallTimeoutSeconds = stallTimeoutSeconds;
            MaxTimeoutRecoveries = maxTimeoutRecoveries;
        }
    }

    internal readonly struct RoadRoutePreviewPalette
    {
        public readonly byte PaletteId;
        public readonly string Label;
        public readonly float WidthMeters;
        public readonly float BorderWidthMeters;
        public readonly Vector4 FillColor;
        public readonly Vector4 BorderColor;

        public RoadRoutePreviewPalette(
            byte paletteId,
            string label,
            float widthMeters,
            float borderWidthMeters,
            in Vector4 fillColor,
            in Vector4 borderColor)
        {
            PaletteId = paletteId;
            Label = label;
            WidthMeters = widthMeters;
            BorderWidthMeters = borderWidthMeters;
            FillColor = fillColor;
            BorderColor = borderColor;
        }
    }

    internal sealed class RoadRouteProfileCatalog
    {
        public const byte PlannerDefault = 1;
        public const byte PlannerNorthScout = 2;
        public const byte PlannerSouthGuard = 3;

        public const byte ExecutionVanguard = 1;
        public const byte ExecutionCourier = 2;
        public const byte ExecutionSiege = 3;

        public const byte PreviewAmber = 1;
        public const byte PreviewCyan = 2;
        public const byte PreviewCrimson = 3;

        private readonly World _world;

        public RoadRouteProfileCatalog(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public RoadRoutePlannerProfile ResolvePlanner(Entity actor)
        {
            byte presetId = ResolveProfile(actor).PlannerPresetId;
            return presetId switch
            {
                PlannerNorthScout => new RoadRoutePlannerProfile(
                    presetId,
                    label: "North Scout",
                    agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                    corridorPreference: RoadRouteCorridorPreference.PreferNorth,
                    directBiasCm: 18000f,
                    northBiasCm: -6000f,
                    southBiasCm: 26000f,
                    allowNorthVariant: true,
                    allowSouthVariant: false),
                PlannerSouthGuard => new RoadRoutePlannerProfile(
                    presetId,
                    label: "South Guard",
                    agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                    corridorPreference: RoadRouteCorridorPreference.PreferSouth,
                    directBiasCm: 18000f,
                    northBiasCm: 26000f,
                    southBiasCm: -6000f,
                    allowNorthVariant: false,
                    allowSouthVariant: true),
                _ => new RoadRoutePlannerProfile(
                    PlannerDefault,
                    label: "Grand Road",
                    agentTypeId: RoadNetworkShowcaseIds.PathPlannerAgentTypeId,
                    corridorPreference: RoadRouteCorridorPreference.Direct,
                    directBiasCm: -4000f,
                    northBiasCm: 6000f,
                    southBiasCm: 6000f,
                    allowNorthVariant: true,
                    allowSouthVariant: true),
            };
        }

        public RoadRouteExecutionProfile ResolveExecution(Entity actor)
        {
            byte presetId = ResolveProfile(actor).ExecutionPresetId;
            return presetId switch
            {
                ExecutionCourier => new RoadRouteExecutionProfile(
                    presetId,
                    label: "Courier",
                    waypointRadiusCm: 30f,
                    finalArrivalRadiusCm: 55f,
                    speedMultiplier: 1.22f,
                    minProgressCm: 36f,
                    stallTimeoutSeconds: 0.9f,
                    maxTimeoutRecoveries: 2),
                ExecutionSiege => new RoadRouteExecutionProfile(
                    presetId,
                    label: "Siege Train",
                    waypointRadiusCm: 34f,
                    finalArrivalRadiusCm: 80f,
                    speedMultiplier: 0.68f,
                    minProgressCm: 18f,
                    stallTimeoutSeconds: 2.2f,
                    maxTimeoutRecoveries: 4),
                _ => new RoadRouteExecutionProfile(
                    ExecutionVanguard,
                    label: "Vanguard",
                    waypointRadiusCm: 45f,
                    finalArrivalRadiusCm: 75f,
                    speedMultiplier: 1.0f,
                    minProgressCm: 24f,
                    stallTimeoutSeconds: 1.3f,
                    maxTimeoutRecoveries: 3),
            };
        }

        public RoadRoutePreviewPalette ResolvePreviewPalette(Entity actor)
        {
            byte paletteId = ResolveProfile(actor).PreviewPaletteId;
            return paletteId switch
            {
                PreviewCyan => new RoadRoutePreviewPalette(
                    paletteId,
                    label: "Cyan Preview",
                    widthMeters: 0.72f,
                    borderWidthMeters: 0.05f,
                    fillColor: new Vector4(0.18f, 0.72f, 0.96f, 0.22f),
                    borderColor: new Vector4(0.48f, 0.90f, 1.0f, 0.98f)),
                PreviewCrimson => new RoadRoutePreviewPalette(
                    paletteId,
                    label: "Crimson Preview",
                    widthMeters: 0.72f,
                    borderWidthMeters: 0.05f,
                    fillColor: new Vector4(0.94f, 0.28f, 0.28f, 0.22f),
                    borderColor: new Vector4(1.0f, 0.64f, 0.64f, 0.98f)),
                _ => new RoadRoutePreviewPalette(
                    PreviewAmber,
                    label: "Amber Preview",
                    widthMeters: 0.76f,
                    borderWidthMeters: 0.05f,
                    fillColor: new Vector4(0.98f, 0.78f, 0.28f, 0.24f),
                    borderColor: new Vector4(1.0f, 0.92f, 0.58f, 0.98f)),
            };
        }

        public string Describe(Entity actor)
        {
            RoadRoutePlannerProfile planner = ResolvePlanner(actor);
            RoadRouteExecutionProfile execution = ResolveExecution(actor);
            return $"{planner.Label} | {execution.Label}";
        }

        private RoadMoveProfileRef ResolveProfile(Entity actor)
        {
            if (_world.IsAlive(actor) && _world.Has<RoadMoveProfileRef>(actor))
            {
                return _world.Get<RoadMoveProfileRef>(actor);
            }

            return new RoadMoveProfileRef
            {
                PlannerPresetId = PlannerDefault,
                ExecutionPresetId = ExecutionVanguard,
                PreviewPaletteId = PreviewAmber,
            };
        }
    }
}
