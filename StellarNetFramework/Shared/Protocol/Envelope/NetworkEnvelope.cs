// Assets/StellarNetFramework/Shared/Protocol/Envelope/NetworkEnvelope.cs

using StellarNet.Shared.Identity;

namespace StellarNet.Shared.Protocol.Envelope
{
    // 底层统一传输封套，客户端与服务端复用同一结构，在 Shared 层采用单一定义。
    // 职责边界：只承载底层统一传输所需的公共运行时信息，不承担业务协议字段定义职责。
    // 不得再平行扩展第二套全局封套 / 房间封套定义，避免传输层模型分裂。
    //
    // RoomId 运行时上下文语义：
    //   对 C2SGlobalMessage / S2CGlobalMessage：RoomId 默认允许为空或无效占位值
    //   对 C2SRoomMessage / S2CRoomMessage：RoomId 必须为有效值
    //   RoomId 是运行时上下文字段，不等价于业务协议体中的业务参数字段
    //
    // ConnectionId 运行时注入约束：
    //   ConnectionId 不属于本封套字段定义，属于服务端接收链的运行时上下文。
    //   服务端在收到客户端消息后从底层连接上下文中提取，不向客户端暴露。
    //   客户端业务逻辑不得持有或上传有效 ConnectionId 作为身份依据。
    public sealed class NetworkEnvelope
    {
        // 协议唯一标识 ID，用于 MessageRegistry 查找对应协议类型
        public int MessageId { get; set; }

        // 序列化后的协议体字节数组
        public byte[] Payload { get; set; }

        // 运行时房间上下文字段。
        // 房间域消息必须为有效值；全局域消息允许为空字符串或 null。
        // 此字段由发送链在进入 Adapter 前完成绑定，不由开发者协议体直接填充。
        public string RoomId { get; set; }

        public NetworkEnvelope()
        {
            RoomId = string.Empty;
        }

        public NetworkEnvelope(int messageId, byte[] payload, string roomId = "")
        {
            MessageId = messageId;
            Payload = payload;
            RoomId = roomId ?? string.Empty;
        }

        // 判断当前封套是否携带有效房间上下文
        public bool HasValidRoomId => !string.IsNullOrEmpty(RoomId);

        public override string ToString()
        {
            return $"NetworkEnvelope(MessageId={MessageId}, RoomId={RoomId}, PayloadLength={Payload?.Length ?? 0})";
        }
    }
}