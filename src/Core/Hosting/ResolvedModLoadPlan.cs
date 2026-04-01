using System;
using System.Collections.Generic;

namespace Ludots.Core.Hosting
{
    public sealed record ResolvedModLoadEntry(
        string Id,
        string RootPath);

    public sealed record ResolvedModLoadPlan(
        IReadOnlyList<ResolvedModLoadEntry> OrderedMods,
        int? SchemaVersion = null,
        string? PlanFingerprint = null,
        string? GeneratedAtUtc = null,
        string? GraphPath = null)
    {
        public static ResolvedModLoadPlan CreateExplicit(IReadOnlyList<ResolvedModLoadEntry> orderedMods)
        {
            if (orderedMods == null)
            {
                throw new ArgumentNullException(nameof(orderedMods));
            }

            return new ResolvedModLoadPlan(orderedMods);
        }
    }
}
