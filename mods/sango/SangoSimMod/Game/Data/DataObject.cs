namespace Sango.Core
{
    /// <summary>
    /// 数据对象抽象类，实现IDataObject接口
    /// </summary>
    public abstract class DataObject : IDataObject
    {
        /// <summary>
        /// 从二进制读取器加载数据
        /// </summary>
        /// <param name="node">二进制读取器</param>
        public virtual void Load(System.IO.BinaryReader node) {; }
        /// <summary>
        /// 保存数据到二进制写入器
        /// </summary>
        /// <param name="node">二进制写入器</param>
        public virtual void Save(System.IO.BinaryWriter node) {; }
        /// <summary>
        /// 保存数据到XML节点
        /// </summary>
        /// <param name="node">XML节点</param>
        public virtual void Save(System.Xml.XmlNode node) {; }
        /// <summary>
        /// 从XML节点加载数据
        /// </summary>
        /// <param name="node">XML节点</param>
        public virtual void Load(System.Xml.XmlNode node) {; }
        /// <summary>
        /// 保存数据到JSON节点
        /// </summary>
        /// <param name="node">JSON节点</param>
        public virtual void Save(SimpleJSON.JSONNode node) {; }
        /// <summary>
        /// 从JSON节点加载数据
        /// </summary>
        /// <param name="node">JSON节点</param>
        public virtual void Load(SimpleJSON.JSONNode node) {; }
    }
}
