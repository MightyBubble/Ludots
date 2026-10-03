using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// navmesh.json 的 links[] 严格校验。
    ///
    /// Link 是跨表面连接的正式边：端点必须落在已声明 layer 上，允许的 profile 必须已声明，
    /// 未知键一律拒绝。校验只发生在加载期；运行期不得猜测或降级为直线补救。
    /// </summary>
    internal sealed class NavLinkConfigValidator
    {
        private const int MaxAuthoredLinks = 4096;

        private readonly Func<string, bool> _hasLayer;
        private readonly Func<string, bool> _hasAgentProfile;

        public NavLinkConfigValidator(Func<string, bool> hasLayer, Func<string, bool> hasAgentProfile)
        {
            _hasLayer = hasLayer ?? throw new ArgumentNullException(nameof(hasLayer));
            _hasAgentProfile = hasAgentProfile ?? throw new ArgumentNullException(nameof(hasAgentProfile));
        }

        public void Validate(JsonArray links, string path)
        {
            if (links.Count > MaxAuthoredLinks)
            {
                throw new InvalidOperationException(
                    $"{path} declares {links.Count} links, exceeding the authored link budget {MaxAuthoredLinks}.");
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i] is not JsonObject link)
                {
                    throw new InvalidOperationException($"{path}[{i}] must be an object.");
                }

                string linkPath = $"{path}[{i}]";
                NavMeshJsonStrict.RequireOnlyProperties(
                    link,
                    linkPath,
                    new[] { "id" },
                    new[] { "from", "to", "bidirectional", "allowedProfiles", "cost", "action" });

                string id = NavMeshJsonStrict.RequireString(link, "id", linkPath);
                if (!seenIds.Add(id))
                {
                    throw new InvalidOperationException($"{path} contains duplicate link id '{id}'.");
                }

                ValidateEndpoint(link, "from", linkPath, id);
                ValidateEndpoint(link, "to", linkPath, id);
                ValidateOptionalScalars(link, linkPath);
                ValidateAllowedProfiles(link, linkPath, id);
            }
        }

        private void ValidateEndpoint(JsonObject link, string key, string linkPath, string linkId)
        {
            if (!link.TryGetPropertyValue(key, out JsonNode? endpointNode) || endpointNode is null)
            {
                throw new InvalidOperationException($"{linkPath} requires an explicit '{key}' endpoint.");
            }

            if (endpointNode is not JsonObject endpoint)
            {
                throw new InvalidOperationException($"{linkPath}.{key} must be an object.");
            }

            string endpointPath = $"{linkPath}.{key}";
            NavMeshJsonStrict.RequireOnlyProperties(endpoint, endpointPath, "layer", "xCm", "yCm");

            string layer = NavMeshJsonStrict.RequireString(endpoint, "layer", endpointPath);
            if (!_hasLayer(layer))
            {
                throw new InvalidOperationException(
                    $"NavMesh link '{linkId}' {key} endpoint references undeclared layer '{layer}'.");
            }

            NavMeshJsonStrict.RequireInt(endpoint, "xCm", endpointPath);
            NavMeshJsonStrict.RequireInt(endpoint, "yCm", endpointPath);
        }

        private static void ValidateOptionalScalars(JsonObject link, string linkPath)
        {
            if (link.TryGetPropertyValue("bidirectional", out JsonNode? bidirectionalNode) &&
                bidirectionalNode is not null &&
                !NavMeshJsonStrict.IsBoolean(bidirectionalNode))
            {
                throw new InvalidOperationException($"{linkPath}.bidirectional must be a boolean.");
            }

            if (link.TryGetPropertyValue("cost", out JsonNode? costNode) && costNode is not null)
            {
                float cost = NavMeshJsonStrict.RequireFloat(link, "cost", linkPath);
                if (float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f)
                {
                    throw new InvalidOperationException($"{linkPath}.cost must be a finite value >= 0.");
                }
            }

            if (link.TryGetPropertyValue("action", out JsonNode? actionNode) && actionNode is not null)
            {
                NavMeshJsonStrict.RequireString(link, "action", linkPath);
            }
        }

        private void ValidateAllowedProfiles(JsonObject link, string linkPath, string linkId)
        {
            if (!link.TryGetPropertyValue("allowedProfiles", out JsonNode? profilesNode) || profilesNode is null)
            {
                return;
            }

            if (profilesNode is not JsonArray profiles)
            {
                throw new InvalidOperationException($"{linkPath}.allowedProfiles must be an array.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i] is null || profiles[i]!.GetValueKind() != System.Text.Json.JsonValueKind.String)
                {
                    throw new InvalidOperationException($"{linkPath}.allowedProfiles[{i}] must be a string.");
                }

                string profileId = profiles[i]!.GetValue<string>();
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    throw new InvalidOperationException($"{linkPath}.allowedProfiles[{i}] must be non-empty.");
                }

                if (!seen.Add(profileId))
                {
                    throw new InvalidOperationException(
                        $"NavMesh link '{linkId}' declares duplicate allowed profile '{profileId}'.");
                }

                if (!_hasAgentProfile(profileId))
                {
                    throw new InvalidOperationException(
                        $"NavMesh link '{linkId}' allows undeclared agent profile '{profileId}'.");
                }
            }
        }
    }
}
