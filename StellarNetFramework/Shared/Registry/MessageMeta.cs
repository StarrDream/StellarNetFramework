// Assets/StellarNetFramework/Shared/Registry/MessageMeta.cs

using System;
using StellarNet.Shared.Enums;

namespace StellarNet.Shared.Registry
{
    // 单条协议注册元数据，由 MessageRegistryBuilder 在启动扫描阶段构建并写入 MessageRegistry。
    // 运行期只读，禁止任何运行时修改。
    // Direction 与 Domain 必须由四协议基类直接推导，不允许开发者通过额外字段覆盖。
    // MessageRegistry 负责记录推导结果，不负责重新解释或重写协议类型语义。
    public sealed class MessageMeta
    {
        // 协议唯一标识 ID，来源于 MessageIdAttribute
        public int MessageId { get; }

        // 协议运行时类型，用于反序列化与类型系统查找
        public Type MessageType { get; }

        // 协议方向，由四协议基类直接推导，C2S 或 S2C
        public MessageDirection Direction { get; }

        // 协议域归属，由四协议基类直接推导，Global 或 Room
        public MessageDomain Domain { get; }

        // 协议投递语义，来源于 DeliveryModeAttribute
        public DeliveryMode DeliveryMode { get; }

        public MessageMeta(
            int messageId,
            Type messageType,
            MessageDirection direction,
            MessageDomain domain,
            DeliveryMode deliveryMode)
        {
            MessageId = messageId;
            MessageType = messageType;
            Direction = direction;
            Domain = domain;
            DeliveryMode = deliveryMode;
        }

        public override string ToString()
        {
            return $"MessageMeta(Id={MessageId}, Type={MessageType?.Name}, " +
                   $"Direction={Direction}, Domain={Domain}, Delivery={DeliveryMode})";
        }
    }
}