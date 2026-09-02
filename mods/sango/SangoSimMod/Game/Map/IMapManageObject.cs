using UnityEngine;
namespace Sango.Core
{
    /// <summary>
    /// 地图管理对象接口
    /// </summary>
    public interface IMapManageObject
    {
        /// <summary>
        /// 地图渲染管理器
        /// </summary>
        Sango.Render.MapRender manager { get; set; }
        /// <summary>
        /// 边界
        /// </summary>
        Sango.Tools.Rect bounds { get; set; }
        /// <summary>
        /// 世界边界
        /// </summary>
        Sango.Tools.Rect worldBounds { get;}
        /// <summary>
        /// 变换组件
        /// </summary>
        UnityEngine.Transform transform { get; }

        /// <summary>
        /// 对象ID
        /// </summary>
        int objId { get; set; }
 
        /// <summary>
        /// 对象类型
        /// </summary>
        int objType { get; set; }
        /// <summary>
        /// 绑定ID
        /// </summary>
        int bindId { get; set; }
        /// <summary>
        /// 模型资源
        /// </summary>
        string modelAsset { get; set; }

        /// <summary>
        /// 是否已经添加
        /// </summary>
        bool isAdded { get; set; }

        /// <summary>
        /// 模型ID
        /// </summary>
        int modelId { get; set; }
        /// <summary>
        /// 是否可见
        /// </summary>
        bool visible { get; set; }
        /// <summary>
        /// 是否静态
        /// </summary>
        bool isStatic { get; set; }
        /// <summary>
        /// 是否可选择
        /// </summary>
        bool selectable { get; set; }
        /// <summary>
        /// 是否保持在视图中
        /// </summary>
        bool remainInView { get; set; }
        /// <summary>
        /// 是否可保存至地图
        /// </summary>
        bool canSave { get; set; }
        /// <summary>
        /// 位置
        /// </summary>
        Vector3 position { get; set; }
        /// <summary>
        /// 旋转
        /// </summary>
        Vector3 rotation { get; set; }
        /// <summary>
        /// 前向方向
        /// </summary>
        Vector3 forward { get; set; }
        /// <summary>
        /// 缩放
        /// </summary>
        Vector3 scale { get; set; }
        /// <summary>
        /// 坐标
        /// </summary>
        Vector2Int coords { get; set; }
        /// <summary>
        /// 点击事件
        /// </summary>
        void OnClick();
        /// <summary>
        /// 检查是否与矩形重叠
        /// </summary>
        /// <param name="rect">矩形</param>
        /// <returns>是否重叠</returns>
        bool Overlaps(Sango.Tools.Rect rect);
        /// <summary>
        /// 鼠标进入事件
        /// </summary>
        void OnPointerEnter();
        /// <summary>
        /// 鼠标退出事件
        /// </summary>
        void OnPointerExit();
        /// <summary>
        /// 设置轮廓显示
        /// </summary>
        /// <param name="material">材质</param>
        void SetOutlineShow(Material material);
        /// <summary>
        /// 编辑器显示
        /// </summary>
        /// <param name="b">是否显示</param>
        void EditorShow(bool b);
        /// <summary>
        /// 设置父对象
        /// </summary>
        /// <param name="parent">父对象</param>
        void SetParent(Transform parent);
        /// <summary>
        /// 设置父对象
        /// </summary>
        /// <param name="parent">父对象</param>
        /// <param name="worldPositionStays">是否保持世界位置</param>
        void SetParent(Transform parent, bool worldPositionStays);
        /// <summary>
        /// 销毁对象
        /// </summary>
        void Destroy();

        /// <summary>
        /// 获取游戏对象
        /// </summary>
        /// <returns>游戏对象</returns>
        GameObject GetGameObject();

        /// <summary>
        /// 创建模型
        /// </summary>
        /// <param name="meshFile">网格文件</param>
        /// <param name="textureFile">纹理文件</param>
        /// <param name="shaderName">着色器名称</param>
        /// <param name="isShareMat">是否共享材质</param>
        public void CreateModel(string meshFile, string textureFile, string shaderName, bool isShareMat = true);
        /// <summary>
        /// 创建模型
        /// </summary>
        /// <param name="packagePath">包路径</param>
        /// <param name="assetName">资源名称</param>
        public void CreateModel(string packagePath, string assetName);
        /// <summary>
        /// 创建模型
        /// </summary>
        /// <param name="assetName">资源名称</param>
        public void CreateModel(string assetName);
        /// <summary>
        /// 创建模型
        /// </summary>
        /// <param name="modelObj">模型对象</param>
        public void CreateModel(UnityEngine.Object modelObj);
        /// <summary>
        /// 更改模型
        /// </summary>
        /// <param name="newAsset">新资源</param>
        public void ChangeModel(string newAsset);

        /// <summary>
        /// 重新加载模型
        /// </summary>
        /// <param name="checkAsset">是否检查资源</param>
        public void ReLoadModels(bool checkAsset = true);

        /// <summary>
        /// 清除模型
        /// </summary>
        public void ClearModels();
        /// <summary>
        /// 重新检查可见性
        /// </summary>
        public void ReCheckVisible();
    }
}
