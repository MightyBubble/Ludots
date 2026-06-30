using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Morph.Config
{
    public sealed class MorphProfileConfig
    {
        public string Id { get; set; } = string.Empty;
        public string? Placement { get; set; }
        public string? StableIdPolicy { get; set; }
        public bool? DestroySource { get; set; }
        public MorphProfileInheritConfig? Inherit { get; set; }
    }

    public sealed class MorphProfileInheritConfig
    {
        public List<string>? Identity { get; set; }
        public MorphProfileAttributeInheritConfig? Attributes { get; set; }
        public MorphProfileTagInheritConfig? Tags { get; set; }
        public MorphProfileEffectInheritConfig? Effects { get; set; }
        public MorphProfileSelectionInheritConfig? Selection { get; set; }
    }

    public sealed class MorphProfileAttributeInheritConfig
    {
        public string? Mode { get; set; }
        public string? Source { get; set; }
        public List<string>? Names { get; set; }
    }

    public sealed class MorphProfileTagInheritConfig
    {
        public string? Mode { get; set; }
        public List<string>? Carry { get; set; }
        public List<string>? Strip { get; set; }
    }

    public sealed class MorphProfileEffectInheritConfig
    {
        public string? Mode { get; set; }
    }

    public sealed class MorphProfileSelectionInheritConfig
    {
        public bool? ReplaceSourceInAllSets { get; set; }
    }
}
