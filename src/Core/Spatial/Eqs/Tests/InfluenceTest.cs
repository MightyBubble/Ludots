using System;

namespace Ludots.Core.Spatial.Eqs.Tests
{
    /// <summary>
    /// Score by sampling a named influence field at the candidate position.
    /// Reads only; never writes the field (EQS boundary contract).
    /// </summary>
    public sealed class InfluenceTest : IEqsTest
    {
        private readonly string _fieldKey;
        private readonly bool _preferLow;
        private readonly float _weight;
        private readonly float _normalizeScale;
        private readonly bool _filterAboveThreshold;
        private readonly float _threshold;

        /// <param name="fieldKey">Registered influence field key (e.g. "threat", "opportunity").</param>
        /// <param name="preferLow">true: lower influence = higher score (avoid threat); false: higher = better.</param>
        /// <param name="weight">Score contribution weight.</param>
        /// <param name="normalizeScale">Value used to normalize raw influence to 0..1 (peak expected).</param>
        /// <param name="filterAboveThreshold">If true, filter out candidates whose influence exceeds threshold.</param>
        /// <param name="threshold">Hard filter threshold when filterAboveThreshold is set.</param>
        public InfluenceTest(
            string fieldKey,
            bool preferLow,
            float weight = 1f,
            float normalizeScale = 1f,
            bool filterAboveThreshold = false,
            float threshold = 0f)
        {
            _fieldKey = fieldKey ?? throw new ArgumentNullException(nameof(fieldKey));
            _preferLow = preferLow;
            _weight = weight;
            _normalizeScale = normalizeScale <= 0f ? 1f : normalizeScale;
            _filterAboveThreshold = filterAboveThreshold;
            _threshold = threshold;
        }

        public void Score(in EqsContext ctx, ref EqsItem item)
        {
            if (item.Filtered || ctx.InfluenceFields == null)
            {
                return;
            }

            if (!ctx.InfluenceFields.TryGet(_fieldKey, out var field))
            {
                return; // field not registered; no contribution
            }

            float raw = field.Sample(item.Position);

            if (_filterAboveThreshold && raw > _threshold)
            {
                item.Filtered = true;
                return;
            }

            float normalized = Math.Clamp(raw / _normalizeScale, 0f, 1f);
            float scoreContribution = _preferLow ? (1f - normalized) : normalized;
            item.Score += scoreContribution * _weight;
        }
    }
}
