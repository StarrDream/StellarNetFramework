// Assets/StellarNetFramework/Shared/Protocol/Attributes/DeliveryModeAttribute.cs

using System;
using StellarNet.Shared.Enums;

namespace StellarNet.Shared.Protocol.Attributes
{
    // 协议投递语义声明特性，必须与 MessageIdAttribute 分开独立声明。
    // 带有 [MessageId] 的协议类型必须同时显式声明此特性，
    // 缺失时 MessageRegistryBuilder 在启动扫描阶段直接报错阻断。
    // 职责单一：只承载投递语义声明，不承载任何其他协议元数据。
    // 该设计为未来扩展其他协议元数据特性保留干净空间。
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DeliveryModeAttribute : Attribute
    {
        // 当前协议的投递语义
        public DeliveryMode Mode { get; }

        public DeliveryModeAttribute(DeliveryMode mode)
        {
            Mode = mode;
        }
    }
}