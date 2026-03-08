using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 房间基础设置组件内置协议聚合脚本
    // 框架保留号段：6000 - 6999
    // 覆盖：房间基础信息同步、设置修改、房主操作、成员列表同步、准备状态协调
    // 房间基础设置组件是每个房间实例成立所必需的骨架级业务组件，创建房间时强制挂载
    // 成员加入 / 离开通知属于房间内部骨架状态变化，也归此组件负责
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端请求修改房间基础设置（房间名、简介、密码等）。
    /// 属于房间域上行协议，仅房主有权限操作，服务端做权限校验。
    /// </summary>
    [MessageId(6000)]
    public sealed class C2S_UpdateRoomBaseSettings : C2SRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 新房间名，null 表示不修改。
        /// </summary>
        public string NewRoomName;

        /// <summary>
        /// 新房间简介，null 表示不修改。
        /// </summary>
        public string NewDescription;

        /// <summary>
        /// 新密码，null 表示不修改，空字符串表示取消密码。
        /// </summary>
        public string NewPassword;

        /// <summary>
        /// 新最大成员数，0 表示不修改。
        /// </summary>
        public int NewMaxMemberCount;
    }

    /// <summary>
    /// 服务端广播房间基础设置已变更。
    /// 属于房间域下行协议，广播给房间全体成员。
    /// </summary>
    [MessageId(6001)]
    public sealed class S2C_RoomBaseSettingsUpdated : S2CRoomMessage
    {
        public string RoomId;
        public string RoomName;
        public string Description;
        public bool HasPassword;
        public int MaxMemberCount;
    }

    /// <summary>
    /// 客户端请求将房主权限转让给指定成员。
    /// 属于房间域上行协议，仅当前房主有权限操作。
    /// </summary>
    [MessageId(6002)]
    public sealed class C2S_TransferRoomOwner : C2SRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 目标新房主的 SessionId。
        /// </summary>
        public string TargetSessionId;
    }

    /// <summary>
    /// 服务端广播房主已变更。
    /// 属于房间域下行协议，广播给房间全体成员。
    /// </summary>
    [MessageId(6003)]
    public sealed class S2C_RoomOwnerChanged : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 新房主的 SessionId。
        /// </summary>
        public string NewOwnerSessionId;
    }

    /// <summary>
    /// 客户端（房主）请求踢出指定成员。
    /// 属于房间域上行协议，仅房主有权限操作。
    /// </summary>
    [MessageId(6004)]
    public sealed class C2S_KickRoomMember : C2SRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 被踢出的目标成员 SessionId。
        /// </summary>
        public string TargetSessionId;
    }

    /// <summary>
    /// 服务端通知被踢出的成员本人。
    /// 属于房间域下行协议，单播给被踢出的成员。
    /// </summary>
    [MessageId(6005)]
    public sealed class S2C_KickedFromRoom : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 执行踢出操作的房主 SessionId。
        /// </summary>
        public string ByOwnerSessionId;
    }

    /// <summary>
    /// 服务端下发当前房间成员列表快照。
    /// 属于房间域下行协议，在成员加入/离开/重连后广播给全体成员，或单播给新加入成员。
    /// </summary>
    [MessageId(6006)]
    public sealed class S2C_RoomMemberListSnapshot : S2CRoomMessage
    {
        public string RoomId;
        public RoomMemberSnapshot[] Members;
    }

    /// <summary>
    /// 房间成员快照，包含成员当前完整状态。
    /// </summary>
    public sealed class RoomMemberSnapshot
    {
        public string SessionId;
        public bool IsOnline;
        public bool IsRoomOwner;
        public bool IsReady;
    }

    /// <summary>
    /// 客户端请求修改自身准备状态。
    /// 属于房间域上行协议。
    /// </summary>
    [MessageId(6007)]
    public sealed class C2S_SetReadyState : C2SRoomMessage
    {
        public string RoomId;
        public bool IsReady;
    }

    /// <summary>
    /// 服务端广播某成员准备状态已变更。
    /// 属于房间域下行协议，广播给房间全体成员。
    /// </summary>
    [MessageId(6008)]
    public sealed class S2C_MemberReadyStateChanged : S2CRoomMessage
    {
        public string RoomId;
        public string SessionId;
        public bool IsReady;
    }

    /// <summary>
    /// 服务端广播房间可开始条件已满足或不再满足。
    /// 属于房间域下行协议，广播给房间全体成员。
    /// 具体可开始条件由开发者在房间基础设置组件中实现，框架只提供广播出口。
    /// </summary>
    [MessageId(6009)]
    public sealed class S2C_RoomCanStartStateChanged : S2CRoomMessage
    {
        public string RoomId;

        /// <summary>
        /// 当前是否满足开始条件。
        /// </summary>
        public bool CanStart;
    }

    /// <summary>
    /// 服务端广播房间基础状态快照，用于新加入成员或重连成员的初始状态恢复。
    /// 属于房间域下行协议，单播给目标成员。
    /// </summary>
    [MessageId(6010)]
    public sealed class S2C_RoomBaseSettingsSnapshot : S2CRoomMessage
    {
        public string RoomId;
        public string RoomName;
        public string Description;
        public bool HasPassword;
        public int MaxMemberCount;
        public string OwnerSessionId;
        public bool CanStart;
    }

    /// <summary>
    /// 服务端通知房间内所有成员某新成员已加入。
    /// 属于房间域下行协议，广播给当前房间全体成员。
    /// 这是房间内部成员集合变化通知，归属房间基础设置组件。
    /// </summary>
    [MessageId(6011)]
    public sealed class S2C_MemberJoined : S2CRoomMessage
    {
        /// <summary>
        /// 新加入的成员 SessionId。
        /// </summary>
        public string SessionId;
    }

    /// <summary>
    /// 服务端通知房间内所有成员某成员已离开。
    /// 属于房间域下行协议，广播给当前房间全体成员。
    /// 这是房间内部成员集合变化通知，归属房间基础设置组件。
    /// </summary>
    [MessageId(6012)]
    public sealed class S2C_MemberLeft : S2CRoomMessage
    {
        /// <summary>
        /// 离开的成员 SessionId。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 离开原因：主动离开、被踢出、断线等。
        /// </summary>
        public string Reason;
    }
}