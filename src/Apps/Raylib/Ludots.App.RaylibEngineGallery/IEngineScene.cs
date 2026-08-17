using Raylib_cs;

namespace Ludots.App.RaylibEngineGallery
{
    /// <summary>
    /// 引擎画廊标准场景合同：一个能力一个场景，自含可读，零 Ludots.Core 依赖。
    /// </summary>
    public interface IEngineScene : IDisposable
    {
        string Id { get; }

        string Title { get; }

        string Summary { get; }

        /// <summary>初始化 GPU 资源；在窗口与 GL 上下文就绪后调用一次。</summary>
        void Load();

        /// <summary>每帧绘制；可调整相机（默认轨道相机由画廊提供）。</summary>
        void Draw(float deltaSeconds, double totalTimeSeconds, ref Camera3D camera);
    }
}
