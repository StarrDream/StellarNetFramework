namespace StellarNet.Shared.Envelope
{
    /// <summary>
    /// 统一底层传输封套，承载底层统一传输所需的公共运行时信息。
    /// 不承担业务协议字段定义职责，不等价于协议层四协议基类模型。
    /// RoomId 属于运行时房间上下文字段：
    ///   - 全局域消息允许为空或默认值
    ///   - 房间域消息必须为有效值
    /// ConnectionId 不属于此封套字段，属于服务端接收链的运行时上下文，由 Adapter 层注入。
    /// </summary>
    public sealed class NetworkEnvelope
    {
        /// <summary>
        /// 协议唯一标识，用于接收链反序列化查表与发送链序列化查表。
        /// </summary>
        public int MessageId;

        /// <summary>
        /// 协议体序列化后的字节负载。
        /// </summary>
        public byte[] Payload;

        /// <summary>
        /// 房间运行时上下文标识。
        /// 全局域消息此字段允许为空字符串或默认值。
        /// 房间域消息此字段必须为有效 RoomId 字符串。
        /// 此字段不等价于开发者业务协议中定义的业务参数字段。
        /// </summary>
        public string RoomId;

        public NetworkEnvelope() { }

        public NetworkEnvelope(int messageId, byte[] payload, string roomId = "")
        {
            MessageId = messageId;
            Payload = payload;
            RoomId = roomId ?? string.Empty;
        }
    }
}