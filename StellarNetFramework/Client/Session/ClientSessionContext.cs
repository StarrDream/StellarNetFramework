// Assets/StellarNetFramework/Client/Session/ClientSessionContext.cs

using StellarNet.Shared.Identity;

namespace StellarNet.Client.Session
{
    // 客户端会话上下文，记录本客户端当前的会话状态。
    // 由服务端认证成功后下发 SessionId，客户端持久保存用于重连凭证。
    // 不等价于服务端 SessionData，客户端只持有自身视角的会话信息。
    // 不负责持久化，持久化由业务层决定（如 PlayerPrefs 或本地文件）。
    public sealed class ClientSessionContext
    {
        // 服务端签发的会话唯一标识，认证成功后写入，断线重连时作为凭证上传
        public SessionId SessionId { get; private set; }

        // 当前所在房间 ID，加房成功后写入，离房或断线时清空
        public string CurrentRoomId { get; private set; }

        // 是否已完成认证（持有有效 SessionId）
        public bool IsAuthenticated => SessionId.IsValid;

        // 是否当前在房间内
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);

        // 当前连接状态，由 ClientInfrastructure 在连接/断开事件时更新
        public bool IsConnected { get; private set; }

        public ClientSessionContext()
        {
            SessionId = SessionId.Invalid;
            CurrentRoomId = string.Empty;
            IsConnected = false;
        }

        // 认证成功后写入服务端签发的 SessionId
        public void OnAuthenticated(SessionId sessionId)
        {
            SessionId = sessionId;
        }

        // 连接建立时更新连接状态
        public void OnConnected()
        {
            IsConnected = true;
        }

        // 连接断开时更新连接状态，保留 SessionId 用于重连
        public void OnDisconnected()
        {
            IsConnected = false;
        }

        // 加房成功后写入房间 ID
        public void OnJoinedRoom(string roomId)
        {
            CurrentRoomId = roomId ?? string.Empty;
        }

        // 离房或被踢出后清空房间 ID
        public void OnLeftRoom()
        {
            CurrentRoomId = string.Empty;
        }

        // 重置全部会话状态，用于登出或重新认证
        public void Reset()
        {
            SessionId = SessionId.Invalid;
            CurrentRoomId = string.Empty;
            IsConnected = false;
        }

        public override string ToString()
        {
            return $"ClientSessionContext(SessionId={SessionId}, " +
                   $"RoomId={CurrentRoomId}, IsConnected={IsConnected}, " +
                   $"IsAuthenticated={IsAuthenticated})";
        }
    }
}
