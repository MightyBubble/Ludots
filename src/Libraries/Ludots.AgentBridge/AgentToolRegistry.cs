using System.Text.Json.Nodes;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Registry of agent-callable tools. Follows the repository Registry
    /// pattern: duplicate names fail loudly, unknown names fail explicitly.
    /// Mods register additional tools at OnLoad time through the
    /// AgentBridgeMod extension point.
    /// </summary>
    public sealed class AgentToolRegistry
    {
        private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, IAgentTool> Tools => _tools;

        public void Register(IAgentTool tool)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                throw new ArgumentException("Tool name must not be empty.", nameof(tool));
            }

            if (!_tools.TryAdd(tool.Name, tool))
            {
                throw new InvalidOperationException(
                    $"Agent tool '{tool.Name}' is already registered by '{_tools[tool.Name].GetType().FullName}'.");
            }
        }

        public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool!);

        public JsonArray DescribeAll()
        {
            var array = new JsonArray();
            foreach (KeyValuePair<string, IAgentTool> pair in _tools.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                array.Add(new JsonObject
                {
                    ["name"] = pair.Value.Name,
                    ["description"] = pair.Value.Description,
                    ["inputSchema"] = pair.Value.InputSchema?.DeepClone(),
                });
            }

            return array;
        }
    }
}
