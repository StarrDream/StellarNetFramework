using UnityEngine;

namespace StellarNet.Client.Session
{
    /// <summary>
    /// 客户端会话上下文，持有客户端本地的 SessionId 与 CurrentRoomId。
    /// SessionId 由服务端签发，客户端在登录成功后写入，断线重连时携带此值发起重连请求。
    /// CurrentRoomId 在加入房间成功后写入，离开房间后清空。
    /// 此上下文是客户端唯一的会话状态持有者，不允许在其他地方分散持有 SessionId。
    /// 客户端不持有服务端 SessionRecord，只持有此轻量级上下文。
    /// </summary>
    public sealed class ClientSessionContext
    {
        /// <summary>
        /// 服务端签发的会话唯一标识。
        /// 登录成功前为空字符串，登录成功后写入，断线重连时携带此值。
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>
        /// 当前所在房间的 RoomId。
        /// 不在任何房间时为空字符串。
        /// </summary>
        public string CurrentRoomId { get; private set; }

        /// <summary>
        /// 当前是否持有有效 SessionId（已登录状态）。
        /// </summary>
        public bool IsLoggedIn => !string.IsNullOrEmpty(SessionId);

        /// <summary>
        /// 当前是否在房间中。
        /// </summary>
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);

        public ClientSessionContext()
        {
            SessionId = string.Empty;
            CurrentRoomId = string.Empty;
        }

        /// <summary>
        /// 写入服务端签发的 SessionId，在登录成功时调用。
        /// </summary>
        public void SetSessionId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("[ClientSessionContext] SetSessionId 失败：sessionId 为空，当前 SessionId 不变。");
                return;
            }
            SessionId = sessionId;
        }

        /// <summary>
        /// 写入当前房间 RoomId，在加入房间成功时调用。
        /// </summary>
        public void SetCurrentRoomId(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ClientSessionContext] SetCurrentRoomId 失败：roomId 为空，当前 CurrentRoomId 不变。");
                return;
            }
            CurrentRoomId = roomId;
        }

        /// <summary>
        /// 清空当前房间 RoomId，在离开房间或房间销毁时调用。
        /// </summary>
        public void ClearCurrentRoomId()
        {
            CurrentRoomId = string.Empty;
        }

        /// <summary>
        /// 清空 SessionId，在登出或重连失败需要重新登录时调用。
        /// 清空 SessionId 时同步清空 CurrentRoomId。
        /// </summary>
        public void ClearSession()
        {
            SessionId = string.Empty;
            CurrentRoomId = string.Empty;
        }
    }
}
