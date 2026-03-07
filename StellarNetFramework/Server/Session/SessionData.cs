// Assets/StellarNetFramework/Server/Session/SessionData.cs

using StellarNet.Shared.Identity;

namespace StellarNet.Server.Session
{
    // 服务端单条会话数据，由 SessionManager 持有与管理。
    // 记录一个已认证用户的完整运行时会话状态。
    // SessionId 由服务端签发，不等价于账号 ID、角色 ID 或客户端本地生成 ID。
    // 断线后 SessionData 可继续保留到其独立超时结束，不随连接断开立即销毁。
    public sealed class SessionData
    {
        // 服务端签发的会话唯一标识
        public SessionId SessionId { get; }

        // 当前绑定的框架连接标识。
        // 断线后置为 ConnectionId.Invalid，重连接管时替换为新连接。
        public ConnectionId ConnectionId { get; set; }

        // 当前所在房间 ID。
        // 未进入任何房间时为 null 或空字符串。
        // 房间因空置超时被销毁时，SessionManager 必须清空此字段。
        public string CurrentRoomId { get; set; }

        // 会话是否已被标记为 Replaced（旧连接被新连接接管）。
        // 被标记后，该旧连接的所有后续来包一律拒收。
        public bool IsReplaced { get; set; }

        // 会话最后活跃时间戳（Unix 毫秒），用于超时判定
        public long LastActiveUnixMs { get; set; }

        // 会话创建时间戳（Unix 毫秒），用于诊断与日志
        public long CreatedUnixMs { get; }

        // 开发者可扩展的用户业务数据挂载位。
        // 框架不解析此字段，仅负责在会话生命周期内保留。
        // 典型用途：账号 ID、角色名、权限等级等业务身份信息。
        public object UserData { get; set; }

        public SessionData(SessionId sessionId, ConnectionId connectionId, long createdUnixMs)
        {
            SessionId = sessionId;
            ConnectionId = connectionId;
            CreatedUnixMs = createdUnixMs;
            LastActiveUnixMs = createdUnixMs;
            CurrentRoomId = string.Empty;
            IsReplaced = false;
            UserData = null;
        }

        // 判断当前会话是否处于在线状态（持有有效连接且未被接管）
        public bool IsOnline => ConnectionId.IsValid && !IsReplaced;

        // 判断当前会话是否处于房间内
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);

        public override string ToString()
        {
            return $"SessionData(SessionId={SessionId}, ConnectionId={ConnectionId}, " +
                   $"RoomId={CurrentRoomId}, IsOnline={IsOnline}, IsReplaced={IsReplaced})";
        }
    }
}