using StellarNet.Shared.Identity;

namespace StellarNet.Server.Session
{
    /// <summary>
    /// 单条会话记录，保存服务端维护的会话运行时状态。
    /// SessionId 由服务端签发，不等价于账号 ID 或客户端本地生成 ID。
    /// HasRoomBinding 只表示曾经绑定过房间，IsInRoom 才表示当前在线且可正常进行房间通信。
    /// 禁止业务仅凭 CurrentRoomId 非空就判定当前可进行房间通信，必须同时检查 IsInRoom。
    /// 断线后 ConnectionId 可以失效，SessionId 可以保留用于重连恢复。
    /// </summary>
    public sealed class SessionRecord
    {
        /// <summary>
        /// 服务端签发的会话唯一标识。
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 当前绑定的底层连接标识。
        /// 断线后此值变为 ConnectionId.Invalid，但 SessionId 仍可保留。
        /// 重连接管成功后此值替换为新连接的 ConnectionId。
        /// </summary>
        public ConnectionId CurrentConnectionId { get; private set; }

        /// <summary>
        /// 当前会话所在的房间 RoomId。
        /// 空字符串表示当前不在任何房间中。
        /// 仅凭此字段非空不能判定当前可进行房间通信，必须同时检查 IsInRoom。
        /// </summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>
        /// 会话是否曾经绑定过房间。
        /// 此字段只表示历史绑定状态，不表示当前在线可通信状态。
        /// </summary>
        public bool HasRoomBinding => !string.IsNullOrEmpty(CurrentRoomId);

        /// <summary>
        /// 当前会话是否在线且可正常进行房间通信。
        /// 必须同时满足：底层连接有效 + 当前在房间中。
        /// </summary>
        public bool IsInRoom => CurrentConnectionId.IsValid && !string.IsNullOrEmpty(CurrentRoomId);

        /// <summary>
        /// 当前底层连接是否有效（在线状态）。
        /// </summary>
        public bool IsOnline => CurrentConnectionId.IsValid;

        /// <summary>
        /// 会话创建时间（Unix 毫秒时间戳），用于超时判定。
        /// </summary>
        public long CreateUnixMs { get; }

        /// <summary>
        /// 最后一次活跃时间（Unix 毫秒时间戳），用于 Session 保留超时计算。
        /// </summary>
        public long LastActiveUnixMs { get; private set; }

        /// <summary>
        /// 会话是否已被标记为 Replaced（被新连接接管后旧连接的状态）。
        /// 被标记为 Replaced 的旧连接，其后续所有来包一律拒收。
        /// </summary>
        public bool IsReplaced { get; private set; }

        public SessionRecord(string sessionId, ConnectionId connectionId)
        {
            SessionId = sessionId;
            CurrentConnectionId = connectionId;
            CurrentRoomId = string.Empty;
            CreateUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LastActiveUnixMs = CreateUnixMs;
            IsReplaced = false;
        }

        /// <summary>
        /// 更新底层连接标识，用于重连接管时替换旧连接。
        /// 任意时刻一个 SessionId 只允许存在一个有效主连接。
        /// </summary>
        public void UpdateConnection(ConnectionId newConnectionId)
        {
            CurrentConnectionId = newConnectionId;
            RefreshActiveTime();
        }

        /// <summary>
        /// 绑定房间，在成员加入房间时调用。
        /// </summary>
        public void BindRoom(string roomId)
        {
            CurrentRoomId = roomId ?? string.Empty;
            RefreshActiveTime();
        }

        /// <summary>
        /// 解绑房间，在成员离开房间或房间销毁时调用。
        /// 解绑后 CurrentRoomId 清空，HasRoomBinding 变为 false。
        /// </summary>
        public void UnbindRoom()
        {
            CurrentRoomId = string.Empty;
            RefreshActiveTime();
        }

        /// <summary>
        /// 标记底层连接已断开，ConnectionId 置为 Invalid。
        /// SessionId 仍保留，用于后续重连恢复。
        /// </summary>
        public void MarkDisconnected()
        {
            CurrentConnectionId = ConnectionId.Invalid;
            RefreshActiveTime();
        }

        /// <summary>
        /// 将此会话标记为已被新连接接管（Replaced）。
        /// 被标记后，此旧连接的后续所有来包一律拒收。
        /// </summary>
        public void MarkReplaced()
        {
            IsReplaced = true;
            CurrentConnectionId = ConnectionId.Invalid;
        }

        /// <summary>
        /// 刷新最后活跃时间，在任意有效操作后调用。
        /// </summary>
        public void RefreshActiveTime()
        {
            LastActiveUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}