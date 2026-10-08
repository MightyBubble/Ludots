using System;

namespace Ludots.Core.Navigation.AgentProfiles
{
    public sealed class AgentProfileConfig
    {
        public string Id { get; set; } = string.Empty;
        public float RadiusCm { get; set; }
        public float HeightCm { get; set; }
        public float ClearanceCm { get; set; }
        public float DraftCm { get; set; }
        public float BeamCm { get; set; }
        public float Mass { get; set; }
        public int Layer { get; set; }

        public void Validate(int index)
        {
            string path = $"AgentProfile[{index}]";
            if (string.IsNullOrWhiteSpace(Id))
            {
                throw new InvalidOperationException($"{path}.id must be a non-empty canonical string.");
            }

            if (!string.Equals(Id.Trim(), Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.id must not contain leading or trailing whitespace.");
            }

            RequirePositive(RadiusCm, nameof(RadiusCm), Id);
            RequirePositive(HeightCm, nameof(HeightCm), Id);
            if (ClearanceCm < 0f || float.IsNaN(ClearanceCm))
            {
                throw new InvalidOperationException($"AgentProfile '{Id}' requires ClearanceCm >= 0.");
            }
            if (DraftCm < 0f || float.IsNaN(DraftCm))
            {
                throw new InvalidOperationException($"AgentProfile '{Id}' requires DraftCm >= 0.");
            }
            if (BeamCm < 0f || float.IsNaN(BeamCm))
            {
                throw new InvalidOperationException($"AgentProfile '{Id}' requires BeamCm >= 0.");
            }

            RequirePositive(Mass, nameof(Mass), Id);
            if (Layer < 0)
            {
                throw new InvalidOperationException($"AgentProfile '{Id}' requires Layer >= 0.");
            }
        }

        private static void RequirePositive(float value, string field, string id)
        {
            if (!(value > 0f) || float.IsNaN(value))
            {
                throw new InvalidOperationException($"AgentProfile '{id}' requires {field} > 0.");
            }
        }
    }
}
