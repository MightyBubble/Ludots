using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public sealed class AttributeBindingConfig : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string Attribute { get; set; } = string.Empty;
        public string Sink { get; set; } = string.Empty;
        public int Channel { get; set; }
        public string Mode { get; set; } = "Add";
        public float Scale { get; set; } = 1f;
        public string ResetPolicy { get; set; } = "None";
    }
}
