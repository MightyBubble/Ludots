using UnityEngine;
namespace Sango.Core
{
    /// <summary>
    /// 地图渲染接口
    /// </summary>
    public interface IMapRender
    {
        /// <summary>
        /// 添加动态对象
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        public void AddDynamic(IMapManageObject obj);
        /// <summary>
        /// 添加静态对象
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        public void AddStatic(IMapManageObject obj);
        /// <summary>
        /// 移除动态对象
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        public void Remove(IMapManageObject obj);

        /// <summary>
        /// 添加实例对象
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        public void AddInstance(IMapManageObject obj);
        /// <summary>
        /// 移除实例对象
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        public void RemoveInstance(IMapManageObject obj);
        /// <summary>
        /// 检查对象是否在视图中
        /// </summary>
        /// <param name="obj">地图管理对象</param>
        /// <returns>是否在视图中</returns>
        public bool IsInView(IMapManageObject obj);
    }
}
