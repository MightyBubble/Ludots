using System;
using Ludots.Core.Fields.Influence;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial.Eqs.Tests;

namespace Ludots.Core.Spatial.Eqs.Config
{
    public sealed class EqsInfluenceConfigDocument
    {
        public EqsInfluenceConfigDocument(
            InfluenceFieldConfig[] fields,
            EqsQueryConfig[] queries,
            EqsScenarioConfig[] scenarios)
        {
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            Queries = queries ?? throw new ArgumentNullException(nameof(queries));
            Scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));
        }

        public InfluenceFieldConfig[] Fields { get; }
        public EqsQueryConfig[] Queries { get; }
        public EqsScenarioConfig[] Scenarios { get; }
    }

    public sealed class InfluenceFieldConfig
    {
        public InfluenceFieldConfig(
            string id,
            int cellSizeCm,
            int chunkSizeCells,
            float defaultValue,
            InfluenceSourceConfig[] sources)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            CellSizeCm = cellSizeCm;
            ChunkSizeCells = chunkSizeCells;
            DefaultValue = defaultValue;
            Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        }

        public string Id { get; }
        public int CellSizeCm { get; }
        public int ChunkSizeCells { get; }
        public float DefaultValue { get; }
        public InfluenceSourceConfig[] Sources { get; }
    }

    public sealed class InfluenceSourceConfig
    {
        public InfluenceSourceConfig(int xCm, int yCm, int radiusCm, float peak, FalloffKind falloff)
        {
            XCm = xCm;
            YCm = yCm;
            RadiusCm = radiusCm;
            Peak = peak;
            Falloff = falloff;
        }

        public int XCm { get; }
        public int YCm { get; }
        public int RadiusCm { get; }
        public float Peak { get; }
        public FalloffKind Falloff { get; }

        public WorldCmInt2 Position => new(XCm, YCm);
    }

    public sealed class EqsQueryConfig
    {
        public EqsQueryConfig(
            string id,
            EqsGeneratorConfig generator,
            EqsTestConfig[] tests,
            EqsSelectionConfig selection)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Tests = tests ?? throw new ArgumentNullException(nameof(tests));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        }

        public string Id { get; }
        public EqsGeneratorConfig Generator { get; }
        public EqsTestConfig[] Tests { get; }
        public EqsSelectionConfig Selection { get; }
    }

    public sealed class EqsGeneratorConfig
    {
        public EqsGeneratorConfig(string kind, int extentCm, int cellSizeCm, int radiusCm, int innerCm, int outerCm, int count)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            ExtentCm = extentCm;
            CellSizeCm = cellSizeCm;
            RadiusCm = radiusCm;
            InnerCm = innerCm;
            OuterCm = outerCm;
            Count = count;
        }

        public string Kind { get; }
        public int ExtentCm { get; }
        public int CellSizeCm { get; }
        public int RadiusCm { get; }
        public int InnerCm { get; }
        public int OuterCm { get; }
        public int Count { get; }
    }

    public sealed class EqsTestConfig
    {
        public EqsTestConfig(
            string kind,
            bool preferNear,
            bool preferLow,
            bool preferMore,
            float weight,
            float normalizeScale,
            int normalizeCount,
            string? fieldKey,
            OverlapShape overlapShape,
            int extentCm,
            WorldCmInt2? reference)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            PreferNear = preferNear;
            PreferLow = preferLow;
            PreferMore = preferMore;
            Weight = weight;
            NormalizeScale = normalizeScale;
            NormalizeCount = normalizeCount;
            FieldKey = fieldKey;
            OverlapShape = overlapShape;
            ExtentCm = extentCm;
            Reference = reference;
        }

        public string Kind { get; }
        public bool PreferNear { get; }
        public bool PreferLow { get; }
        public bool PreferMore { get; }
        public float Weight { get; }
        public float NormalizeScale { get; }
        public int NormalizeCount { get; }
        public string? FieldKey { get; }
        public OverlapShape OverlapShape { get; }
        public int ExtentCm { get; }
        public WorldCmInt2? Reference { get; }
    }

    public sealed class EqsSelectionConfig
    {
        public EqsSelectionConfig(string kind, int topN, float threshold)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            TopN = topN;
            Threshold = threshold;
        }

        public string Kind { get; }
        public int TopN { get; }
        public float Threshold { get; }
    }

    public sealed class EqsScenarioConfig
    {
        public EqsScenarioConfig(
            string id,
            WorldCmInt2 origin,
            string queryId,
            string[] influenceFieldIds,
            EqsPresentationConfig presentation)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Origin = origin;
            QueryId = queryId ?? throw new ArgumentNullException(nameof(queryId));
            InfluenceFieldIds = influenceFieldIds ?? throw new ArgumentNullException(nameof(influenceFieldIds));
            Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

        public string Id { get; }
        public WorldCmInt2 Origin { get; }
        public string QueryId { get; }
        public string[] InfluenceFieldIds { get; }
        public EqsPresentationConfig Presentation { get; }
    }

    public sealed class EqsPresentationConfig
    {
        public EqsPresentationConfig(
            string influenceFieldId,
            bool drawCandidates,
            bool drawBest,
            float normalizePeak)
        {
            InfluenceFieldId = influenceFieldId ?? throw new ArgumentNullException(nameof(influenceFieldId));
            DrawCandidates = drawCandidates;
            DrawBest = drawBest;
            NormalizePeak = normalizePeak;
        }

        public string InfluenceFieldId { get; }
        public bool DrawCandidates { get; }
        public bool DrawBest { get; }
        public float NormalizePeak { get; }
    }
}
