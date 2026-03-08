using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 断线重连模块内置协议聚合脚本
    // 框架保留号段：1000 - 1999
    // 覆盖：重连请求、重连结果、房间业务单元清单下发、恢复完成确认
    // C2S_Reconnect 明确注册为 C2SGlobalMessage，即使目标状态为房间内也属于全局域协议
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端发起重连请求。
    /// 属于全局域上行协议，明确注册为 C2SGlobalMessage。
    /// 客户端在底层连接建立后、本地持有有效 SessionId 时发送此协议，而非重新登录。
    /// 重连是原会话与原房间上下文的权威恢复流程，不是重新登录一次。
    /// </summary>
    [MessageId(1000)]
    public sealed class C2S_Reconnect : C2SGlobalMessage
    {
        /// <summary>
        /// 客户端持有的会话标识，由服务端在上次登录成功时签发。
        /// 服务端凭此校验会话合法性并完成连接接管。
        /// </summary>
        public string SessionId;
    }

    /// <summary>
    /// 服务端返回重连结果。
    /// 属于全局域下行协议，始终属于 S2CGlobalMessage，即使携带房间恢复信息也不转化为房间域协议。
    /// 当目标状态为房间内时，允许直接内嵌 RoomId 与房间业务组件清单，
    /// 这些字段只用于帮助客户端确定目标状态并执行房间装配预处理，不等价于业务组件状态快照。
    /// </summary>
    [MessageId(1001)]
    public sealed class S2C_ReconnectResult : S2CGlobalMessage
    {
        /// <summary>
        /// 重连是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 失败原因，仅在 Success=false 时有效。
        /// 凭证无效时客户端应清除本地 SessionId 并退回登录入口。
        /// </summary>
        public string FailReason;

        /// <summary>
        /// 重连后的目标客户端状态。
        /// 服务端根据原会话所处阶段决定：InLobby 或 InRoom。
        /// </summary>
        public string TargetState;

        /// <summary>
        /// 目标恢复房间的 RoomId，仅当 TargetState=InRoom 时有效。
        /// 此字段属于业务字段，与 NetworkEnvelope.RoomId 的运行时上下文语义彼此独立，不得混淆。
        /// </summary>
        public string TargetRoomId;

        /// <summary>
        /// 房间业务组件清单，仅当 TargetState=InRoom 时有效。
        /// 客户端根据此清单重建本地房间结构，顺序即装配顺序。
        /// 使用稳定组件注册标识，不使用运行时类型名。
        /// </summary>
        public string[] RoomComponentIds;
    }

    /// <summary>
    /// 客户端通知服务端本地房间结构重建完成，请求各业务单元下发恢复快照。
    /// 属于房间域上行协议，在客户端完成本地房间装配后发送。
    /// 服务端收到后通知房间实例进入恢复协调流程，各业务单元向该重连客户端提供最新快照。
    /// </summary>
    [MessageId(1002)]
    public sealed class C2S_ReconnectRoomReady : C2SRoomMessage
    {
        /// <summary>
        /// 目标恢复房间的 RoomId，服务端用于校验房间归属一致性。
        /// </summary>
        public string TargetRoomId;
    }

    /// <summary>
    /// 服务端通知客户端重连恢复流程已全部完成，可正式进入正常局内流程。
    /// 属于房间域下行协议，在所有业务单元快照下发完毕后广播给该重连客户端。
    /// </summary>
    [MessageId(1003)]
    public sealed class S2C_ReconnectRecoveryComplete : S2CRoomMessage
    {
        /// <summary>
        /// 恢复完成的房间 RoomId，客户端用于校验当前房间上下文一致性。
        /// </summary>
        public string RoomId;
    }
}
