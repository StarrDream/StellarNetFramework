// ════════════════════════════════════════════════════════════════
// 文件：RoomDispatchBuiltInMessages.cs
// 路径：Assets/StellarNetFramework/Runtime/Shared/Protocol/BuiltIn/Room/RoomDispatchBuiltInMessages.cs
// 职责：房间调度模块内置协议定义。
//       修正：S2C_CreateRoomResult 增加 RoomComponentIds 字段，
//       确保客户端建房后能依据服务端权威清单进行装配。
// ════════════════════════════════════════════════════════════════

using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 房间调度模块内置协议聚合脚本
    // 框架保留号段：2000 - 2999
    // 覆盖：创建房间、加入房间、离开房间、获取房间列表、获取房间信息、获取成员列表
    // 所有房间调度协议属于全局域，不依赖当前房间归属上下文
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端请求创建房间。
    /// 属于全局域上行协议，必须携带客户端本地生成的幂等 Token 防止重复建房。
    /// 服务端通过 IdempotentCache 以 Token 为键做防重处理。
    /// </summary>
    [MessageId(2000)]
    public sealed class C2S_CreateRoom : C2SGlobalMessage
    {
        /// <summary>
        /// 客户端本地生成的请求幂等 Token，首次触发建房意图时生成，
        /// 在收到最终响应前所有超时重试都必须复用同一 Token。
        /// </summary>
        public string IdempotentToken;

        /// <summary>
        /// 房间名称。
        /// </summary>
        public string RoomName;

        /// <summary>
        /// 房间密码，空字符串表示无密码。
        /// </summary>
        public string Password;

        /// <summary>
        /// 最大成员数量。
        /// </summary>
        public int MaxMemberCount;
    }

    /// <summary>
    /// 服务端返回创建房间结果。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(2001)]
    public sealed class S2C_CreateRoomResult : S2CGlobalMessage
    {
        /// <summary>
        /// 创建是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 创建成功时的房间 RoomId，客户端凭此进入房间装配流程。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// [关键字段] 房间业务组件清单。
        /// 服务端创建房间时会强制挂载 RoomBaseSettings 等基础组件。
        /// 客户端必须使用此清单进行本地装配，严禁在本地硬编码默认组件。
        /// </summary>
        public string[] RoomComponentIds;

        /// <summary>
        /// 失败原因，仅在 Success=false 时有效。
        /// </summary>
        public string FailReason;
    }

    /// <summary>
    /// 客户端请求加入指定房间。
    /// 属于全局域上行协议，不依赖当前房间归属上下文。
    /// </summary>
    [MessageId(2002)]
    public sealed class C2S_JoinRoom : C2SGlobalMessage
    {
        /// <summary>
        /// 目标房间的 RoomId。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间密码，无密码房间传空字符串。
        /// </summary>
        public string Password;
    }

    /// <summary>
    /// 服务端返回加入房间结果。
    /// 属于全局域下行协议。
    /// 加入成功时携带房间业务组件清单，客户端据此执行本地房间装配。
    /// </summary>
    [MessageId(2003)]
    public sealed class S2C_JoinRoomResult : S2CGlobalMessage
    {
        /// <summary>
        /// 加入是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 加入成功时的房间 RoomId。
        /// </summary>
        public string RoomId;

        /// <summary>
        /// 房间业务组件清单，客户端根据此清单重建本地房间结构，顺序即装配顺序。
        /// 使用稳定组件注册标识，不使用运行时类型名。
        /// </summary>
        public string[] RoomComponentIds;

        /// <summary>
        /// 失败原因，仅在 Success=false 时有效。
        /// </summary>
        public string FailReason;
    }

    /// <summary>
    /// 客户端请求主动离开当前房间。
    /// 属于房间域上行协议，必须绑定有效房间上下文。
    /// </summary>
    [MessageId(2004)]
    public sealed class C2S_LeaveRoom : C2SRoomMessage
    {
        /// <summary>
        /// 当前所在房间的 RoomId，服务端用于校验房间归属一致性。
        /// </summary>
        public string RoomId;
    }

    /// <summary>
    /// 服务端返回离开房间结果。
    /// 属于全局域下行协议（因为离房后已无房间上下文）。
    /// 用于通知客户端状态机安全切换回大厅。
    /// </summary>
    [MessageId(2005)]
    public sealed class S2C_LeaveRoomResult : S2CGlobalMessage
    {
        public bool Success;
    }

    /// <summary>
    /// 客户端请求获取当前可加入的房间列表。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(2007)]
    public sealed class C2S_GetRoomList : C2SGlobalMessage
    {
        /// <summary>
        /// 分页起始索引，从 0 开始。
        /// </summary>
        public int PageIndex;

        /// <summary>
        /// 每页数量。
        /// </summary>
        public int PageSize;
    }

    /// <summary>
    /// 服务端返回房间列表。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(2008)]
    public sealed class S2C_RoomListResult : S2CGlobalMessage
    {
        /// <summary>
        /// 房间基础信息列表。
        /// </summary>
        public RoomBriefInfo[] Rooms;

        /// <summary>
        /// 当前总房间数量，用于分页计算。
        /// </summary>
        public int TotalCount;
    }

    /// <summary>
    /// 房间列表中单条房间的基础信息快照，用于大厅展示。
    /// </summary>
    public sealed class RoomBriefInfo
    {
        public string RoomId;
        public string RoomName;
        public int CurrentMemberCount;
        public int MaxMemberCount;
        public bool HasPassword;
    }

    /// <summary>
    /// 客户端请求获取指定房间的基础信息。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(2009)]
    public sealed class C2S_GetRoomInfo : C2SGlobalMessage
    {
        public string RoomId;
    }

    /// <summary>
    /// 服务端返回指定房间的基础信息。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(2010)]
    public sealed class S2C_RoomInfoResult : S2CGlobalMessage
    {
        public bool Success;
        public string RoomId;
        public string RoomName;
        public int CurrentMemberCount;
        public int MaxMemberCount;
        public bool HasPassword;
        public string FailReason;
    }

    /// <summary>
    /// 客户端请求获取当前房间的成员列表。
    /// 属于房间域上行协议，必须绑定有效房间上下文。
    /// </summary>
    [MessageId(2011)]
    public sealed class C2S_GetRoomMemberList : C2SRoomMessage
    {
        public string RoomId;
    }

    /// <summary>
    /// 服务端返回当前房间的成员列表。
    /// 属于房间域下行协议，单播给请求方。
    /// </summary>
    [MessageId(2012)]
    public sealed class S2C_RoomMemberListResult : S2CRoomMessage
    {
        public RoomMemberInfo[] Members;
    }

    /// <summary>
    /// 房间成员信息快照，用于成员列表展示。
    /// </summary>
    public sealed class RoomMemberInfo
    {
        public string SessionId;
        public bool IsOnline;
        public bool IsRoomOwner;
    }

    /// <summary>
    /// 服务端通知房间已被销毁或强制解散。
    /// 属于房间域下行协议，广播给当前房间全体成员。
    /// 客户端收到后必须清理局内实例并回到大厅态。
    /// </summary>
    [MessageId(2013)]
    public sealed class S2C_RoomDismissed : S2CRoomMessage
    {
        /// <summary>
        /// 解散原因：房主解散、服务器关闭、空置超时等。
        /// </summary>
        public string Reason;
    }
}