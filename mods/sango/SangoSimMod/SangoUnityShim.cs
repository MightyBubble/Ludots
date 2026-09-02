// Unity-味类型与被裁大件的最小替身,单文件收敛(PLAN §6 裁定;禁止在移植文件里散落替换)。
// 纪律:每个成员都必须有一个已移植文件的实际引用;新增成员须注明需求来源。
// Vector2/Vector3/Vector4 不在此处:GlobalUsings.cs 已映射到 System.Numerics(移植文件中的
// 小写成员访问已逐点改为大写,见汇报)。
// TKNewtonsoft 不在此处:External/TKNewtonsoft/ 以 vendor 库工程整仓提供(fork 的转换器
// 成员上下文扩展是 sango 延迟引用绑定语义的硬依赖)。
// 渲染/窗口/地图场景件(GameAIDebug、ObjectRender 族、MapRender、Window 等)在 Unity 侧
// 是视觉呈现层,Ludots 侧由 raylib/WebUI 承接;此处替身仅保证模拟层编译与 headless 空跑。

using System;
using Sango.Core;

namespace UnityEngine
{
    // Framework/Hex/Hex.cs HexWorld.PositionToCoords / Object 层坐标换算使用。
    public struct Vector2Int
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    // Object 层旗帜/小地图着色与 Json/ColorConverter 序列化使用。
    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color red => new Color(1f, 0f, 0f, 1f);
        public static Color green => new Color(0f, 1f, 0f, 1f);
        public static Color blue => new Color(0f, 0f, 1f, 1f);
        public static Color yellow => new Color(1f, 0.92f, 0.016f, 1f);
        public static Color cyan => new Color(0f, 1f, 1f, 1f);
        public static Color magenta => new Color(1f, 0f, 1f, 1f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);

        public Color(float r, float g, float b) : this(r, g, b, 1f) { }

        // Game/Map/Cell.cs 与 RenderEvent 各事件的色调缩放。
        public static Color operator *(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a * k);
    }

    // Object 层 Force/Troop 标识色与 Json/Color32Converter 序列化使用;索引器供 Color32Converter.ReadJson。
    public struct Color32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public byte this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (index)
                {
                    case 0: r = value; break;
                    case 1: g = value; break;
                    case 2: b = value; break;
                    case 3: a = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    // Json/ColorConverter.WriteJson 使用。
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color)
        {
            return string.Format("{0:X2}{1:X2}{2:X2}",
                (int)(color.r * 255f), (int)(color.g * 255f), (int)(color.b * 255f));
        }
    }

    // Object 层数值计算使用;委托 System.Math 保持 float 语义。
    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = (float)(Math.PI / 180.0);

        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => (float)Math.Abs(f);
        // SkillInstance/DiplomacyManager 的 Mathf.Abs(int) 按整数往返。
        public static int Abs(int v) => Math.Abs(v);

        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
    }

    // Framework/Log/Log.cs 与 Object 层直调 Debug.Log* 使用;输出走 Console。
    public static class Debug
    {
        public static void Log(object message) => Console.Out.WriteLine(message);
        public static void LogWarning(object message) => Console.Out.WriteLine("[Warn] " + message);
        public static void LogError(object message) => Console.Error.WriteLine("[Error] " + message);
    }

    // PLAN D7:UnityEngine.Random 直调统一收敛为 GameRandom 委托;Debate 层(后续波)的
    // Random.Range(int,int)/value 调用是已立案需求。
    public static class Random
    {
        public static int Range(int minInclusive, int maxExclusive) => GameRandom.Range(minInclusive, maxExclusive);
        public static float value => (float)GameRandom.Random();
    }

    // RenderEvent/Events 各事件的"点击跳过演出"分支与 GameAIDebug 的回车步进;
    // headless 恒 false,语义与 IsVisible()==false 分支一致。
    public static class Input
    {
        public static Vector3 mousePosition => default;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetKeyUp(KeyCode key) => false;
    }

    public enum KeyCode
    {
        Return = 13,
    }

    // Scenario/Scenario.cs 演出队列步进使用;默认 0(纯演出事件不耗时)。
    // M1.b:SangoTurnDriver 在推进回合前写入虚拟帧长,让计时型演出事件(DelayEvent 等)
    // 在 headless 下按"一拍完成"结算;默认 0 保留原语义。
    public static class Time
    {
        public static float deltaTime { get; set; }
    }

    // Game/Data/DataLoader.cs 的 Bounds 存取区块使用(center/size 走 System.Numerics)。
    public struct Bounds
    {
        public Vector3 center;
        public Vector3 size;

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }
    }

    // UnityEngine 对象模型基类:IMapManageObject.CreateModel(UnityEngine.Object) 参数需要。
    public class Object
    {
    }

    // BuffManager/GameAIDebug 的视觉资产挂点;headless 下 Find/Instantiate/Load 均为 null,
    // 调用方都有 null 检查或只在 Enabled 时触达。
    public class GameObject : Object
    {
        public Transform transform => null;

        public static GameObject Find(string name) => null;
        public static Object Instantiate(Object original) => null;

        public T GetComponent<T>() => default;
        public void SetActive(bool value) { }
    }

    public class Transform : Object
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }

        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    // GameAIDebug.CreateInfoObject 的 Resources.Load<T>("GridText")。
    public static class Resources
    {
        public static T Load<T>(string path) where T : class => null;
    }

    // Game/Map/IMapManageObject.SetOutlineShow(Material) 参数需要。
    public class Material : Object
    {
    }

    // Game/Map/Cell.cs 的内政模型朝向、GameParticales 调用面。
    public struct Quaternion
    {
        public static Quaternion identity => default;

        public static Quaternion Euler(Vector3 euler) => default;
        public static Quaternion LookRotation(Vector3 forward) => default;
    }

    // PersonSortFunction 的排序标题对齐。
    public enum TextAnchor
    {
        UpperLeft = 0,
        UpperCenter = 1,
        UpperRight = 2,
        MiddleLeft = 4,
        MiddleCenter = 5,
        MiddleRight = 6,
        LowerLeft = 8,
        LowerCenter = 9,
        LowerRight = 10,
    }

    // ModManager 的少量本地偏好(dlc 开关);headless 持内存值。
    public static class PlayerPrefs
    {
        private static readonly System.Collections.Generic.Dictionary<string, int> ints =
            new System.Collections.Generic.Dictionary<string, int>();

        public static int GetInt(string key, int defaultValue = 0)
        {
            return ints.TryGetValue(key, out int v) ? v : defaultValue;
        }

        public static void SetInt(string key, int value) => ints[key] = value;
        public static void Save() { }
    }
}

namespace UnityEngine.UI
{
    // GameAIDebug 的寻路开销浮字(仅 Enabled 时触达)。
    public class Text : UnityEngine.Component
    {
        public string text;
        public UnityEngine.Color color;
        public UnityEngine.GameObject gameObject;
    }
}

namespace UnityEngine
{
    // Text.gameObject 的宿主类型(Unity 侧 Component 是场景件基类)。
    public class Component : Object
    {
    }
}

namespace Sango.Render
{
    using Sango.Core;
    using Sango;
    using UnityEngine;

    // Object 层的渲染引用面(SangoObject.GetRender/Troop.Render/BuffManager/BuildingBase);
    // headless 恒不可见、无挂点。成员集 = 移植文件的实际调用面 + IRender 合同。
    public abstract class ObjectRender : IRender
    {
        public virtual SangoObject Owener { get; set; }
        public MapObject MapObject { get; set; }
        public bool IsVisible() => false;
        public Transform GetTransform() => null;
        public virtual Vector3 GetPosition() => default;
        public virtual void SetPosition(Vector3 pos) { }
        public virtual Vector3 GetForward() => default;
        public virtual void SetForward(Vector3 forward) { }
        public virtual void CastArrow(Vector3 target) { }
        public virtual void Clear() { }
        public virtual void SetFlash(bool b) { }
        public virtual void UpdateRender() { }
        public virtual void ShowInfo(int damage, int damageType) { }
        public virtual void ShowInfo(int damage, int damageType, bool isCrit) { }
        public virtual void ShowSkill(SkillInstance skill, bool isFail, bool isCritical) { }
        public virtual GameObject PlayEffect(string assets) => null;
    }

    // Troop.cs(501)构造;成员为 Troop 层与 RenderEvent 对 Render 的调用面。
    public class TroopRender : ObjectRender
    {
        public TroopRender(Troop troop) { }

        public void SetAniShow(int aniType, bool loop = true) { }
        public void SetSmokeShow(bool show = true) { }
        public void FaceTo(Vector3 position) { }
        public void UpdateModelByCell(Cell destCell) { }
    }

    // Fire.cs(35)构造。
    public class FireRender : ObjectRender
    {
        public FireRender(Fire fire) { }
    }

    // City.cs(877)/Building.cs(150)/Gate.cs(18)/Port.cs(15)构造。
    public class CityRender : ObjectRender { public CityRender(City city) { } }
    public class BuildingRender : ObjectRender { public BuildingRender(Building building) { } }
    public class GateRender : ObjectRender { public GateRender(Gate gate) { } }
    public class PortRender : ObjectRender { public PortRender(Port port) { } }

    // Game/Map/Cell.cs:GridState.Interior / HasGridState(MapGrid.GridState) + 摄像机网格查询。
    public class MapGrid
    {
        public enum GridState : int
        {
            None = 0,
            Defence = 1,
            Interior = 2,
            Thief = 3,
        }

        public float GetGridHeight(int x, int y) => 0f;
        public float GetGridWaterHeight(int x, int y) => 0f;
    }

    // Game/Map/Cell.cs(512)interiorModel = MapObject.Create(...) 及 RenderEvent 的模型字段赋值。
    public class MapObject
    {
        public Sango.Tools.Rect bounds { get; set; }
        public int objType { get; set; }
        public int modelId { get; set; }
        public string modelAsset { get; set; }
        public Vector3 position { get; set; }
        public Transform transform { get; } = new Transform();
        public GameObject gameObject => null;

        public static MapObject Create(string name) => null;
        public void Clear() { }
        public void Destroy() { }
    }

    // Game/Map/Cell.cs 的地形网格顶点(mapData.GetVertexData)。
    public class MapData
    {
        public class VertexData
        {
            public Vector3 position;
        }

        public VertexData GetVertexData(int x, int y) => new VertexData();
    }

    // Scenario.cs(1126/1734)相机状态、OnMapLoaded 世界就绪回调和 GameAIDebug/RenderEvent 的
    // 调试用取景调用;headless 全部空操作。
    public class MapCamera
    {
        public Vector3 position;
        public Vector3 lookRotate;
        public float distance;
    }

    public class MapRender
    {
        public static MapRender Instance { get; } = new MapRender();
        public MapCamera mapCamera { get; } = new MapCamera();
        public MapGrid mapGrid { get; } = new MapGrid();
        public MapData mapData { get; } = new MapData();

        public event Action OnMapLoaded;

        public void FireOnMapLoaded() => OnMapLoaded?.Invoke();

        public void SetCamera(Vector3 position, Vector3 rotation, float distance) { }
        public void SetGridMaskColor(int x, int y, Color color) { }
        public void EndSetGridMask() { }
        public void SetRangeMaskColor(int x, int y, Color color) { }
        public void EndSetRangeMask() { }
        public void AddDynamic(MapObject mapObject) { }
        public void AddStatic(MapObject mapObject) { }
        public void ChangeSeason(int season) { }
        public void Init() { }
        public void LoadMap(string fileName) { }

        public static float QueryHeight(Vector3 pos) => 0f;
        public static bool QueryHeight(Vector3 pos, out float height) { height = 0f; return true; }
        public Vector3 GetCameraPos() => default;
        public void MoveCameraTo(Vector3 position, float duration) { }
    }
}

namespace Sango
{
    // Scenario.cs/WindowEvent/CityFallCompleteEvent/ForceFallCompleteEvent/ObjectsDisplaySystem
    // 的窗口调度;Ludots 侧由 Web UI 承接,替身只保证调用链可编译可空跑。
    public class Window : Singleton<Window>
    {
        public class WindowInterface
        {
            public UGUIWindow ugui_instance;
            public void Open() { }
            public void Open(params object[] objects) { }
            public void Close() { }
            public void Refresh() { }
        }

        private readonly System.Collections.Generic.Dictionary<string, WindowInterface> windows =
            new System.Collections.Generic.Dictionary<string, WindowInterface>();

        public WindowInterface Open(string name)
        {
            return GetWindow(name);
        }

        public WindowInterface Open(string name, params object[] data)
        {
            return GetWindow(name);
        }

        public void Close(string name)
        {
            windows.Remove(name);
        }

        public void CloseAll()
        {
            windows.Clear();
        }

        public void DestroyAll()
        {
            windows.Clear();
        }

        public T Open<T>(string name) where T : class => default;

        public void SetVisible(string name, bool visible) { }

        public void AddPackage(string file, string packageName) { }

        public WindowInterface GetWindow(string name)
        {
            if (!windows.TryGetValue(name, out WindowInterface window))
            {
                window = new WindowInterface();
                windows[name] = window;
            }
            return window;
        }
    }

    // Game/GameDialog.cs 的 IDialog.Window 属性类型;OnCloseAction 供窗口关闭事件使用。
    public class UGUIWindow
    {
        public Action OnCloseAction;
    }

    // Game/GameDialog.cs Next() 的游戏输入开关与 Player 的视控开关组。
    public class GameController : Singleton<GameController>
    {
        public bool Enabled { get; set; }
        public bool KeyboardMoveEnabled { get; set; }
        public bool RotateViewEnabled { get; set; }
        public bool DragMoveViewEnabled { get; set; }
        public bool BorderMoveViewEnabled { get; set; }
        public bool ZoomViewEnabled { get; set; }

        public void Reset()
        {
            KeyboardMoveEnabled = false;
            RotateViewEnabled = false;
            DragMoveViewEnabled = false;
            BorderMoveViewEnabled = false;
            ZoomViewEnabled = false;
        }
    }
}

namespace Sango.Tools
{
    // sango 源里 Sango.Tools.Rect 无本地声明(来自作者外部库,快照缺失);
    // 按 Data/Map 层使用面(center/size/四参构造/Overlaps)补最小替身。
    public struct Rect
    {
        public Vector2 center;
        public Vector2 size;

        public Rect(int x, int y, int width, int height)
        {
            center = new Vector2(x + width / 2f, y + height / 2f);
            size = new Vector2(width, height);
        }

        public bool Overlaps(Rect other)
        {
            return Math.Abs(center.X - other.center.X) < (size.X + other.size.X) / 2f
                && Math.Abs(center.Y - other.center.Y) < (size.Y + other.size.Y) / 2f;
        }
    }
}

namespace Sango
{
    // Object/Skill/Buff/BuffManager.cs 的特效对象池调用;headless 返回 null 即空跑。
    public class PoolManager : System<PoolManager>
    {
        public static UnityEngine.GameObject Create(string assetsPath) => null;
        public static UnityEngine.GameObject Create(string assetsPath, Action<UnityEngine.GameObject> onCreate) => null;
        public static void Recycle(UnityEngine.GameObject obj) { }
    }

    // Game/Mod/ModManager.cs 的 mod 市场下载协作(App.StartCoroutine/GitDownloader)。
    // Ludots 侧 mod 加载走自身 VFS,此处仅保 sango Mod 层可编译可空跑。
    public class App : Singleton<App>
    {
        public void StartCoroutine(System.Collections.IEnumerator routine)
        {
            // 同步排空:headless 下协程链立即走到完成回调(依赖 yield 的市场刷新逻辑本就不该在模拟内核触发)。
            while (routine != null && routine.MoveNext())
            {
            }
        }
    }

    public static class GitDownloader
    {
        // 协程形态(Mod.cs/ModManager.cs 以 StartCoroutine/yield 驱动);签名对齐 Framework/IO
        // 原件;headless 立即回调空结果(结果对象带解压目标路径字段,Mod 层拼接目录用)。
        public class GitDownloadResult
        {
            public string ZipSavePath;
            public string ExtractTargetPath;
        }

        public static System.Collections.IEnumerator Get(string url, Action<float> onProgress, Action<string> onContent)
        {
            onProgress?.Invoke(1f);
            onContent?.Invoke(null);
            yield break;
        }

        public static System.Collections.IEnumerator DownloadAndExtract(string gitUrl, string targetFolder,
            Action<float> onProgress, Action<GitDownloadResult> onComplete)
        {
            onProgress?.Invoke(1f);
            onComplete?.Invoke(new GitDownloadResult { ExtractTargetPath = targetFolder });
            yield break;
        }
    }

    public static class PlatformUtility
    {
        public static string GetPlatformName() => "win64";
    }

    // Mod 层的 HybridCLR 包装载(Framework/Manager 大件,D8 不搬);Ludots 侧无热更包,空跑。
    public class PackageManager : Singleton<PackageManager>
    {
        public void AddPackage(string packageName, string file, bool force) { }
    }

    // sango Framework/IO 的 Path/Directory/File 外壳(D8 大件不搬)。
    // M1.b:读写全部经 SangoVfsIO 网关(Ludots VFS 后端由启动线显式 Install;未安装时
    // 读 fail-fast,存在性判定为假),禁止 shim 直读盘。相对路径按内容 mod 的 assets/ 映射。
    public static class Path
    {
        public static string ContentRootPath { get; set; } = ".";
        public static string SaveRootPath { get; set; } = ".";
        public static string ModRootPath { get; set; } = "./Mods";

        public static void AddSearchPath(string path) { }
        public static void AddSearchPath(string path, bool front) { }

        public static string FindFile(string file) => Runtime.SangoVfsIO.FindFile(file);
    }

    public static class Directory
    {
        public static void EnumFiles(string path, string searchPattern,
            System.IO.SearchOption searchOption, Action<string> onFile)
        {
            if (onFile == null || !Runtime.SangoVfsIO.DirectoryExists(path))
                return;
            foreach (string file in Runtime.SangoVfsIO.EnumerateFiles(path, searchPattern, searchOption))
            {
                onFile(file);
            }
        }

        public static void EnumFiles(string path, string searchPattern, Action<string> onFile)
        {
            EnumFiles(path, searchPattern, System.IO.SearchOption.TopDirectoryOnly, onFile);
        }

        public static void EnumFiles(string path, Action<string> onFile)
        {
            EnumFiles(path, "*", System.IO.SearchOption.TopDirectoryOnly, onFile);
        }

        public static bool Exists(string path) => Runtime.SangoVfsIO.DirectoryExists(path);
        public static void Create(string path) => Runtime.SangoVfsIO.CreateDirectory(path);
        public static void Create(string path, bool recursive) => Runtime.SangoVfsIO.CreateDirectory(path);
        public static void Delete(string path) => Runtime.SangoVfsIO.DeleteDirectory(path, true);
        public static string[] GetDirectories(string path, string searchPattern, System.IO.SearchOption searchOption)
            => Runtime.SangoVfsIO.GetDirectories(path, searchPattern, searchOption);
    }

    public static class File
    {
        public static string ReadAllText(string path) => Runtime.SangoVfsIO.ReadAllText(path);
        public static string[] ReadAllLines(string path) => Runtime.SangoVfsIO.ReadAllLines(path);
        public static System.IO.StreamReader OpenText(string path) => Runtime.SangoVfsIO.OpenText(path);
        public static void WriteAllText(string path, string contents) => Runtime.SangoVfsIO.WriteAllText(path, contents);
        public static void Delete(string path) => Runtime.SangoVfsIO.Delete(path);
        public static bool Exists(string path) => Runtime.SangoVfsIO.Exists(path);
    }
}

namespace Sango.UI
{
    // ObjectsDisplaySystem.OnEnter 把窗口实例转成 uGUI 选择器并 Init;sango UI 层(D8 不搬)
    // 由 Web UI 承接,替身使该路径空跑。
    public class UIObjectSelector : UGUIWindow
    {
        public void Init(object displaySystem) { }
    }

    // DiplomacySystem 的外交窗口(OnOpen 传动作上下文);Web UI 承接前空跑。
    public class UIDiplomacy
    {
        public void OnOpen(object action) { }
    }
}

namespace Sango.Core
{
    using UnityEngine;

    // 音频与特效播放面(D8:音频资产不搬,仓库无音频);签名对齐 Game/GameMedia.cs 源,全部空跑。
    public class GameMedia : Singleton<GameMedia>
    {
        public void Load() { }
        public void Load(string file) { }

        public int PlayVoice(int id) => 0;
        public int PlayVoice(int id, float volume) => 0;
        public int PlaySfx(int id) => 0;
        public int PlaySfx(int id, float volume) => 0;
        public int PlaySfxLoop(int id) => 0;
        public int PlayDelayedSfx(int id, float delay) => 0;
        public void PlayBgm(int id, bool loop = true) { }
        public int PlayButtonSfx() => 0;
        public int PlayCancelSfx() => 0;
        public int PlayDoAcitonSfx() => 0;
        public int PlayMenuClickSfx() => 0;
        public int PlaySubMenuClickSfx() => 0;
        public int PlayNewTurnSfx() => 0;
        public int PlayPersonSay(Person person, int sayId) => 0;
        public void StopLoopSfx() { }
        public void StopBgm() { }
        public void PauseBgm() { }
        public void ResumeBgm() { }
    }

    // Game/GameParticales.cs 与 Game/Render/EffectManager.cs 的粒子/特效播放面;headless 空跑。
    public class GameParticales : Singleton<GameParticales>
    {
        public void PlayEfect(string assets, Vector3 where, float life) { }
        public void PlayEfect(string assets, Vector3 where, Vector3 scale, Quaternion rot, float life) { }
    }

    public class EffectManager
    {
        public static EffectManager Instance { get; } = new EffectManager();

        public UnityEngine.GameObject PlayEffect(string effectName, Vector3 position) => null;
        public void RecycleEffect(string effectName, UnityEngine.GameObject effect) { }
    }
}
