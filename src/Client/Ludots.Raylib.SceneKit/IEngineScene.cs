using System.Numerics;
using Raylib_cs;

namespace Ludots.Raylib.SceneKit
{
    /// <summary>
    /// 引擎层关卡实例合同：装载关卡容器（节点 + 组件 + 资产清单）后组合出的可执行场景。
    /// </summary>
    public interface IEngineScene : IDisposable
    {
        string Id { get; }

        string Title { get; }

        string Summary { get; }

        EngineSceneCameraDefaults CameraDefaults { get; }

        /// <summary>初始化 GPU 资源；在窗口与 GL 上下文就绪后调用一次。</summary>
        void Load();

        /// <summary>每帧绘制；可调整相机（默认轨道相机由播放器提供）。</summary>
        void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera);
    }

    /// <summary>关卡节点上挂载的能力组件；不含关卡元数据，工程文件资产经清单注入。</summary>
    public interface IEngineSceneComponent : IDisposable
    {
        void Load();

        void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera);
    }

    /// <summary>
    /// 消费工程文件资产的组件合同：装载器按关卡资产清单解析出物理路径后注入；
    /// 组件代码因此不出现资产 URI 字面量，清单是装载真源。
    /// </summary>
    public interface IEngineSceneComponentAssets
    {
        void SetAssets(IReadOnlyDictionary<string, EngineSceneAsset> assets);
    }

    /// <summary>
    /// 消费关卡文档组件配置的组件合同：装载器把组件 JSON 对象原样交给组件自行解析；
    /// 配置出现在不可配置的组件上时装载 fail-fast。
    /// </summary>
    public interface IEngineSceneComponentConfigurable
    {
        void Configure(System.Text.Json.JsonElement config);
    }

    /// <summary>关卡资产清单项；Kind 决定解析方式，ResolvedPath 为解析出的物理路径。</summary>
    public sealed record EngineSceneAsset(string Id, string Kind, string Source, string? ResolvedPath);

    /// <summary>组件能力注册：kind 是关卡文件里引用组件的唯一名字，禁止 C# 类型名进关卡。</summary>
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

    public readonly record struct EngineSceneCameraDefaults(
        float Distance,
        float PitchDegrees,
        float YawDegrees,
        Vector3 Target,
        float FovyDegrees);
}
