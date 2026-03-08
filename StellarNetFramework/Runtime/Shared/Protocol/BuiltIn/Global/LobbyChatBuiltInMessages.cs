using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 大厅全局聊天模块内置协议聚合脚本
    // 框架保留号段：5000 - 5999
    // 覆盖：大厅聊天发送、接收、历史消息拉取、系统消息插入、非法消息拦截入口预留
    // 大厅聊天属于全局域，不依赖当前房间归属上下文
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端发送大厅聊天消息。
    /// 属于全局域上行协议。
    /// 服务端收到后做基础非法消息拦截，通过后广播给所有在线大厅客户端。
    /// </summary>
    [MessageId(5000)]
    public sealed class C2S_SendLobbyChat : C2SGlobalMessage
    {
        /// <summary>
        /// 聊天消息内容，服务端会做基础非法内容拦截。
        /// </summary>
        public string Content;
    }

    /// <summary>
    /// 服务端广播大厅聊天消息给所有在线客户端。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(5001)]
    public sealed class S2C_LobbyChatMessage : S2CGlobalMessage
    {
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
        /// 系统消息由服务端直接插入，不经过客户端发送流程。
        /// </summary>
        public int MessageType;
    }

    /// <summary>
    /// 客户端请求拉取大厅聊天历史消息。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(5002)]
    public sealed class C2S_GetLobbyChatHistory : C2SGlobalMessage
    {
        /// <summary>
        /// 请求的历史消息数量，从最新一条向前取。
        /// </summary>
        public int Count;
    }

    /// <summary>
    /// 服务端返回大厅聊天历史消息。
    /// 属于全局域下行协议，单播给请求方。
    /// </summary>
    [MessageId(5003)]
    public sealed class S2C_LobbyChatHistoryResult : S2CGlobalMessage
    {
        /// <summary>
        /// 历史消息列表，按时间从旧到新排列。
        /// </summary>
        public LobbyChatHistoryItem[] Messages;
    }

    /// <summary>
    /// 大厅聊天历史消息条目。
    /// </summary>
    public sealed class LobbyChatHistoryItem
    {
        public string SenderSessionId;
        public string Content;
        public long SendUnixMs;
        public int MessageType;
    }

    /// <summary>
    /// 服务端通知客户端其发送的聊天消息被拦截。
    /// 属于全局域下行协议，单播给被拦截的发送方。
    /// 此协议为基础非法消息拦截接口预留，具体拦截策略由开发者在服务端业务层实现。
    /// </summary>
    [MessageId(5004)]
    public sealed class S2C_LobbyChatBlocked : S2CGlobalMessage
    {
        /// <summary>
        /// 拦截原因，供客户端 UI 层展示。
        /// </summary>
        public string Reason;
    }
}
