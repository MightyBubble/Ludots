using Ludots.Core.Config;

namespace Ludots.Core.Input.Selection
{
    public sealed class SelectionProfileRegistry : DataRegistry<SelectionProfile>
    {
        public SelectionProfileRegistry(ConfigPipeline pipeline) : base(pipeline)
        {
        }
    }
}
