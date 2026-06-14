namespace Ludots.Core.Presentation.Components
{
    public readonly struct PresentationLodEntry
    {
        public PresentationLodEntry(float maxDistanceCm, float minScreenCoverage01)
        {
            MaxDistanceCm = maxDistanceCm;
            MinScreenCoverage01 = minScreenCoverage01;
        }

        public float MaxDistanceCm { get; }

        public float MinScreenCoverage01 { get; }
    }

    public struct PresentationLodProfile
    {
        public PresentationLodProfile(in PresentationLodEntry high, in PresentationLodEntry medium, in PresentationLodEntry low)
        {
            High = high;
            Medium = medium;
            Low = low;
        }

        public PresentationLodEntry High;
        public PresentationLodEntry Medium;
        public PresentationLodEntry Low;
    }
}
