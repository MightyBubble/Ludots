using System.Text.Json;
using System.Numerics;
using Raylib_cs;

namespace Ludots.App.RaylibEngineGallery
{
    /// <summary>
    /// 引擎层关卡实例合同。关卡资产负责组织节点、资产引用和组件；实例只负责执行已装载的关卡。
    /// </summary>
    public interface IEngineScene : IDisposable
    {
        string Id { get; }

        string Title { get; }

        string Summary { get; }

        EngineSceneCameraDefaults CameraDefaults { get; }

        void Load();

        void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera);

    }

    /// <summary>关卡节点上挂载的运行时组件合同；它不是一个关卡，也不包含关卡元数据。</summary>
    public interface IEngineSceneComponent : IDisposable
    {
        void Load();

        void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera);
    }

    public interface IEngineSceneNodeAware
    {
        void SetNodeTransform(in EngineSceneNodeTransform transform);
    }

    public interface IEngineSceneComponentConfigurable
    {
        void Configure(JsonElement config);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class EngineSceneComponentAttribute : Attribute
    {
        public EngineSceneComponentAttribute(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Scene component kind is required.", nameof(kind));
            }

            Kind = kind;
        }

        public string Kind { get; }
    }

    public readonly record struct EngineSceneNodeTransform(
        Vector3 Position,
        Quaternion Rotation,
        Vector3 Scale);
}
