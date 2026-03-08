using System;

namespace StellarNet.Shared.Registry
{
    /// <summary>
    /// 协议注册表元数据结构，记录单条协议的全部注册信息。
    /// Direction 与 Domain 由四协议基类直接推导，不允许额外声明独立配置字段覆盖。
    /// 此结构同时服务于接收链（MessageId → Type）与发送链（Type → MessageId）两侧。
    /// </summary>
    public sealed class MessageMetadata
    {
        /// <summary>
        /// 协议唯一标识 ID，来源于 MessageIdAttribute 声明。
        /// </summary>
        public int MessageId { get; }

        /// <summary>
        /// 协议的 CLR 类型，用于反序列化与类型路由。
        /// </summary>
        public Type MessageType { get; }

        /// <summary>
        /// 消息方向，由四协议基类推导：C2S 或 S2C。
        /// </summary>
        public MessageDirection Direction { get; }

        /// <summary>
        /// 消息域归属，由四协议基类推导：Global 或 Room。
        /// </summary>
        public MessageDomain Domain { get; }

        public MessageMetadata(int messageId, Type messageType, MessageDirection direction, MessageDomain domain)
        {
            MessageId = messageId;
            MessageType = messageType;
            Direction = direction;
            Domain = domain;
        }

        public override string ToString()
        {
            return
                $"[MessageMetadata] Id={MessageId}, Type={MessageType?.Name}, Direction={Direction}, Domain={Domain}";
        }
    }
}