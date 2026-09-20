using System.Collections.Generic;
using Ludots.Core.Config;

namespace Ludots.Core.Fields.Config
{
    /// <summary>
    /// One roster entry of <c>Fields/hierarchies.json</c>: a parent group key with its
    /// member region keys. Strict shape validation happens in <see cref="FieldHierarchyConfigLoader"/>.
    /// </summary>
    public sealed class FieldHierarchyConfig : IIdentifiable
    {
        public string Parent { get; set; } = string.Empty;
        public List<string> Children { get; set; } = new List<string>();

        string IIdentifiable.Id => Parent;
    }
}
