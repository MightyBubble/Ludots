using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sango.Core
{
    /// <summary>
    /// 数据对象接口，定义数据的保存和加载方法
    /// </summary>
    public interface IDataObject
    {
        /// <summary>
        /// 保存数据到XML节点
        /// </summary>
        /// <param name="node">XML节点</param>
        public void Save(System.Xml.XmlNode node);
        /// <summary>
        /// 从XML节点加载数据
        /// </summary>
        /// <param name="node">XML节点</param>
        public void Load(System.Xml.XmlNode node);
        /// <summary>
        /// 保存数据到JSON节点
        /// </summary>
        /// <param name="node">JSON节点</param>
        public void Save(SimpleJSON.JSONNode node);
        /// <summary>
        /// 从JSON节点加载数据
        /// </summary>
        /// <param name="node">JSON节点</param>
        public void Load(SimpleJSON.JSONNode node);
        /// <summary>
        /// 保存数据到二进制写入器
        /// </summary>
        /// <param name="node">二进制写入器</param>
        public void Save(BinaryWriter node);
        /// <summary>
        /// 从二进制读取器加载数据
        /// </summary>
        /// <param name="node">二进制读取器</param>
        public void Load(BinaryReader node);
    }

}
