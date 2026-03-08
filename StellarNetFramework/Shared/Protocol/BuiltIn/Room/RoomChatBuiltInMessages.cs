using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 房间聊天组件内置协议聚合脚本
    // 框架保留号段：7000 - 7999
    // 覆盖：房间聊天发送、接收、历史消息拉取、系统消息插入、频率限制接口预留
    // 房间聊天属于房间域，必须绑定有效房间上下文
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端发送房间聊天消息。
    /// 属于房间域上行协议，必须绑定有效房间上下文。
    /// 服务端收到后做基础非法消息拦截与频率限制检查，通过后广播给房间全体成员。
    /// </summary>
    [MessageId(7000)]
    public sealed class C2S_SendRoomChat : C2SRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 聊天消息内容，服务端会做基础非法内容拦截。
        /// </summary>
        public string Content;
    }

    /// <summary>
    /// 服务端广播房间聊天消息给房间全体成员。
    /// 属于房间域下行协议。
    /// </summary>
    [MessageId(7001)]
    public sealed class S2C_RoomChatMessage : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 发送者的 SessionId。
        /// </summary>
        public string SenderSessionId;

        /// <summary>
        /// 聊天消息内容。
        /// </summary>
        public string Content;

        /// <summary>
        /// 消息发送时间（Unix 毫秒时间戳）。
        /// </summary>
        public long SendUnixMs;

        /// <summary>
        /// 消息类型：0=普通聊天，1=系统消息。
        /// </summary>
        public int MessageType;
    }

    /// <summary>
    /// 客户端请求拉取房间聊天历史消息。
    /// 属于房间域上行协议，必须绑定有效房间上下文。
    /// </summary>
    [MessageId(7002)]
    public sealed class C2S_GetRoomChatHistory : C2SRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 请求的历史消息数量，从最新一条向前取。
        /// </summary>
        public int Count;
    }

    /// <summary>
    /// 服务端返回房间聊天历史消息。
    /// 属于房间域下行协议，单播给请求方。
    /// </summary>
    [MessageId(7003)]
    public sealed class S2C_RoomChatHistoryResult : S2CRoomMessage
    {
        public string RoomId;
        public RoomChatHistoryItem[] Messages;
    }

    /// <summary>
    /// 房间聊天历史消息条目。
    /// </summary>
    public sealed class RoomChatHistoryItem
    {
        public string SenderSessionId;
        public string Content;
        public long SendUnixMs;
        public int MessageType;
    }

    /// <summary>
    /// 服务端通知客户端其发送的房间聊天消息被拦截。
    /// 属于房间域下行协议，单播给被拦截的发送方。
    /// 此协议为基础非法消息拦截接口预留，具体拦截策略由开发者在服务端业务层实现。
    /// </summary>
    [MessageId(7004)]
    public sealed class S2C_RoomChatBlocked : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 拦截原因，供客户端 UI 层展示。
        /// </summary>
        public string Reason;
    }

    /// <summary>
    /// 服务端通知客户端其发送频率超出限制。
    /// 属于房间域下行协议，单播给触发频率限制的发送方。
    /// 此协议为基础频率限制接口预留，具体限制策略由开发者在服务端业务层实现。
    /// </summary>
    [MessageId(7005)]
    public sealed class S2C_RoomChatRateLimited : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 限制解除时间（Unix 毫秒时间戳），客户端 UI 层可据此展示冷却倒计时。
        /// </summary>
        public long CooldownEndUnixMs;
    }
}