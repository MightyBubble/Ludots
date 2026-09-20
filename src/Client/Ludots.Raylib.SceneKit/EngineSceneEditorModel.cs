using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Raylib.SceneKit
{
    /// <summary>
    /// 关卡 JSON 的规范化读写合同：编辑器是 scene.json 唯一写入方，写回必须保持
    /// 规范化形状（indent=2、UTF-8 裸字符、末尾换行）——no-op 装载→保存字节一致由合同测试兜底。
    /// </summary>
    public static class EngineSceneJson
    {
        public static readonly JsonSerializerOptions CanonicalWriter = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static JsonNode LoadNode(string path)
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            return root ?? throw new InvalidDataException($"Engine scene '{path}' is empty.");
        }

        public static string WriteCanonical(JsonNode root)
        {
            // STJ 缩进写出的行尾跟随平台（Windows 为 CRLF）；关卡文件统一 LF，跨平台字节稳定。
            return root.ToJsonString(CanonicalWriter).Replace("\r\n", "\n") + "\n";
        }
    }

    /// <summary>可拾取实例的内存投影与 JSON 寻址（nodes[].components[k].config.instances[i]）。</summary>
    public sealed record EngineEditableInstance(
        string NodeId,
        int ComponentIndex,
        int InstanceIndex,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 HalfExtents,
        JsonArray PositionArray);

    /// <summary>
    /// 场景编辑内存模型：gizmo 只改这里的 JsonNode 投影；显式保存才落盘
    /// （原子替换 + .bak 备份 + mtime 冲突 fail-closed——外部编辑器改过即拒写）。
    /// </summary>
    public sealed class EngineSceneEditorModel
    {
        private readonly string _path;
        private readonly DateTime _loadedLastWriteUtc;
        private JsonNode _root;

        public EngineSceneEditorModel(string scenePath)
        {
            _path = scenePath;
            _loadedLastWriteUtc = File.GetLastWriteTimeUtc(scenePath);
            _root = EngineSceneJson.LoadNode(scenePath);
        }

        public string ScenePath => _path;

        public JsonNode Root => _root;

        /// <summary>枚举 static_mesh 组件的全部实例（拾取目标）。M1 只覆盖 static_mesh 实例。</summary>
        public List<EngineEditableInstance> EnumerateStaticMeshInstances()
        {
            var result = new List<EngineEditableInstance>();
            JsonArray nodes = (JsonArray)_root["nodes"]!;
            foreach (JsonNode node in nodes)
            {
                string nodeId = (string)node["id"]!;
                JsonArray components = (JsonArray)node["components"]!;
                for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
                {
                    JsonNode component = components[componentIndex]!;
                    if ((string?)component["type"] != "static_mesh")
                    {
                        continue;
                    }

                    JsonArray instances = (JsonArray)component["config"]!["instances"]!;
                    for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
                    {
                        JsonNode instance = instances[instanceIndex]!;
                        if (instance["position"] is not JsonArray positionArray || positionArray.Count != 3)
                        {
                            continue;
                        }

                        Vector3 scale = ReadScale(instance);
                        result.Add(new EngineEditableInstance(
                            nodeId,
                            componentIndex,
                            instanceIndex,
                            ReadVector3(positionArray),
                            ReadRotation(instance),
                            scale / 2f,
                            positionArray));
                    }
                }
            }

            return result;
        }

        public void Save()
        {
            DateTime currentLastWrite = File.GetLastWriteTimeUtc(_path);
            if (currentLastWrite != _loadedLastWriteUtc)
            {
                throw new InvalidDataException(
                    $"Engine scene '{_path}' was modified outside the editor after load; save refused (fail closed). Reload the scene and re-apply edits.");
            }

            string temporary = _path + ".tmp";
            string backup = _path + ".bak";
            File.WriteAllText(temporary, EngineSceneJson.WriteCanonical(_root));
            File.Replace(temporary, _path, backup);
        }

        private static Vector3 ReadVector3(JsonArray array)
        {
            return new Vector3(
                (float)(double)array[0]!,
                (float)(double)array[1]!,
                (float)(double)array[2]!);
        }

        private static Vector3 ReadScale(JsonNode instance)
        {
            JsonNode? scale = instance["scale"];
            if (scale is JsonArray array)
            {
                return ReadVector3(array);
            }

            if (scale is JsonValue value && value.TryGetValue<double>(out double uniform))
            {
                return new Vector3((float)uniform);
            }

            return Vector3.One;
        }

        private static Quaternion ReadRotation(JsonNode instance)
        {
            if (instance["yawDeg"] is JsonValue yaw && yaw.TryGetValue<double>(out double degrees))
            {
                return Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)degrees * (MathF.PI / 180f));
            }

            if (instance["rotation"] is JsonArray quaternion && quaternion.Count == 4)
            {
                return new Quaternion(
                    (float)(double)quaternion[0]!,
                    (float)(double)quaternion[1]!,
                    (float)(double)quaternion[2]!,
                    (float)(double)quaternion[3]!);
            }

            return Quaternion.Identity;
        }
    }
}
