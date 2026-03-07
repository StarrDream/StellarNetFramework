// Assets/StellarNetFramework/Shared/Protocol/Attributes/MessageIdAttribute.cs

using System;

namespace StellarNet.Shared.Protocol.Attributes
{
    // 协议 ID 标注特性，直接标注在协议结构体或类上。
    // 框架启动时由 MessageRegistryBuilder 自动扫描并建立类型与 ID 的双向映射。
    // 若发现重复 ID，MessageRegistryBuilder 必须输出 Fatal 日志并抛出异常阻断启动。
    // 号段约定：
    //   0    ~ 9999  : 框架出厂与保留号段，仅供框架基础设施协议使用
    //   10000~       : 开发者自定义号段，供全局模块协议与房间业务组件协议使用
    // 号段只用于 ID 管理，不得作为协议方向或域归属判断依据。
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class MessageIdAttribute : Attribute
    {
        // 协议唯一标识 ID
        public int Id { get; }

        public MessageIdAttribute(int id)
        {
            Id = id;
        }
    }
}