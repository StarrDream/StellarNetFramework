using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Registry;
using UnityEngine;

namespace StellarNet.Server.Network
{
    /// <summary>
    /// 房间归属一致性校验链，是所有 C2SRoomMessage 进入房间业务组件前的强制前置校验节点。
    /// 校验顺序：ConnectionId 有效 → 会话存在 → 会话持有 CurrentRoomId → Envelope.RoomId 与 CurrentRoomId 一致 → 路由到目标房间。
    /// 任意一步失败时直接丢弃消息，严禁继续向房间业务组件派发。
    /// 此校验链只位于 C2SRoomMessage 路径上，C2SGlobalMessage 禁止误入此链。
    /// </summary>
    public sealed class RoomOwnershipValidator
    {
        private readonly SessionManager _sessionManager;

        // 通过委托引用 GlobalRoomManager 的房间路由能力，避免在此层直接持有 GlobalRoomManager 造成循环依赖
        // 委托签名：(roomId, connectionId, metadata, message) → void
        private System.Action<string, ConnectionId, MessageMetadata, object> _roomDispatchDelegate;

        public RoomOwnershipValidator(SessionManager sessionManager)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[RoomOwnershipValidator] 构造失败：sessionManager 为 null。");
                return;
            }
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// 绑定房间分发委托，由 GlobalInfrastructure 在装配阶段注入 GlobalRoomManager 的路由能力。
        /// 采用委托注入而非直接持有，避免循环依赖。
        /// </summary>
        public void BindRoomDispatchDelegate(System.Action<string, ConnectionId, MessageMetadata, object> dispatchDelegate)
        {
            if (dispatchDelegate == null)
            {
                Debug.LogError("[RoomOwnershipValidator] BindRoomDispatchDelegate 失败：dispatchDelegate 为 null。");
                return;
            }
            _roomDispatchDelegate = dispatchDelegate;
        }

        /// <summary>
        /// 执行房间归属一致性校验，校验通过后将消息路由到目标房间实例的 ServerRoomMessageRouter。
        /// 由 ServerNetworkEntry 在确认协议属于 C2SRoomMessage 后调用。
        /// </summary>
        public void Validate(ConnectionId connectionId, string envelopeRoomId, MessageMetadata metadata, object message)
        {
            // 校验步骤 1：ConnectionId 是否有效
            if (!connectionId.IsValid)
            {
                Debug.LogError($"[RoomOwnershipValidator] 校验失败：ConnectionId 无效，MessageId={metadata?.MessageId}，已丢弃。");
                return;
            }

            // 校验步骤 2：ConnectionId 是否已绑定有效会话
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomOwnershipValidator] 校验失败：ConnectionId={connectionId} 未绑定有效会话，MessageId={metadata?.MessageId}，已丢弃。");
                return;
            }

            // 校验步骤 3：会话是否持有有效 CurrentRoomId
            if (string.IsNullOrEmpty(session.CurrentRoomId))
            {
                Debug.LogError($"[RoomOwnershipValidator] 校验失败：SessionId={session.SessionId} 的 CurrentRoomId 为空，" +
                               $"该会话当前不在任何房间中，MessageId={metadata?.MessageId}，ConnectionId={connectionId}，已丢弃。");
                return;
            }

            // 校验步骤 4：Envelope.RoomId 与 Session.CurrentRoomId 完全一致
            if (envelopeRoomId != session.CurrentRoomId)
            {
                Debug.LogError($"[RoomOwnershipValidator] 校验失败：Envelope.RoomId={envelopeRoomId} 与 Session.CurrentRoomId={session.CurrentRoomId} 不一致，" +
                               $"SessionId={session.SessionId}，ConnectionId={connectionId}，MessageId={metadata?.MessageId}，已丢弃。");
                return;
            }

            // 校验步骤 5：路由委托是否已绑定
            if (_roomDispatchDelegate == null)
            {
                Debug.LogError($"[RoomOwnershipValidator] 校验失败：房间分发委托未绑定，无法路由消息，" +
                               $"RoomId={envelopeRoomId}，ConnectionId={connectionId}，MessageId={metadata?.MessageId}，已丢弃。");
                return;
            }

            // 所有校验通过，将消息路由到目标房间实例
            _roomDispatchDelegate.Invoke(envelopeRoomId, connectionId, metadata, message);
        }
    }
}
