using System.Collections.Generic;
using StellarNet.Server.Room;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Server.Sender
{
    /// <summary>
    /// 服务端房间域发送器，只允许发送 S2CRoomMessage 类型协议。
    /// 对外方法显式体现投递范围：房间广播、房间单播、排除发送者广播。
    /// 房间域消息发送时必须显式提供 roomId，发送器不猜测当前房间上下文。
    /// 房间成员展开、目标连接列表计算与广播范围判定在此层完成，Adapter 不认识房间。
    /// 广播目标集合为空时不视为一次有效公共广播发送，输出 Warning 并跳过。
    /// 排除发送者、排除某组成员等变体属于目标集合计算策略，不额外上升为新的框架级发送语义分类。
    /// </summary>
    public sealed class ServerRoomMessageSender
    {
        private readonly ServerSendCoordinator _coordinator;
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _roomManager;

        public ServerRoomMessageSender(
            ServerSendCoordinator coordinator,
            MessageRegistry messageRegistry,
            ISerializer serializer,
            SessionManager sessionManager,
            GlobalRoomManager roomManager)
        {
            if (coordinator == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 构造失败：coordinator 为 null。");
                return;
            }
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 构造失败：messageRegistry 为 null。");
                return;
            }
            if (serializer == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 构造失败：serializer 为 null。");
                return;
            }
            if (sessionManager == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 构造失败：sessionManager 为 null。");
                return;
            }
            if (roomManager == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 构造失败：roomManager 为 null。");
                return;
            }

            _coordinator = coordinator;
            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionManager = sessionManager;
            _roomManager = roomManager;
        }

        /// <summary>
        /// 向房间内所有在线成员广播房间域消息。
        /// 此方法是 Replay 旁路录制的触发点：满足录制条件时，ServerSendCoordinator 自动旁路写入。
        /// 广播目标集合为空时输出 Warning 并跳过，不视为一次有效公共广播。
        /// </summary>
        public void BroadcastToRoom<TMessage>(string roomId, TMessage message)
            where TMessage : S2CRoomMessage
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoom 失败：roomId 为空，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoom 失败：message 为 null，RoomId={roomId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoom 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册，RoomId={roomId}。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoom 失败：消息序列化结果为 null，RoomId={roomId}，MessageId={metadata.MessageId}。");
                return;
            }

            // 通过 GlobalRoomManager 获取房间当前在线连接集合，Adapter 不认识房间
            var targetConnections = _roomManager.GetOnlineConnectionIds(roomId);
            if (targetConnections == null || targetConnections.Count == 0)
            {
                Debug.LogWarning($"[ServerRoomMessageSender] BroadcastToRoom 跳过：房间 {roomId} 当前无在线成员，MessageId={metadata.MessageId}。");
                return;
            }

            // 通过发送协调节点完成投递，协调节点负责 Replay 旁路拦截
            // isBroadcastToAllRoomMembers=true 表示这是一次面向全体房间成员的公共广播，满足录制条件时触发旁路写入
            _coordinator.DispatchRoomBroadcast(targetConnections, metadata.MessageId, payload, roomId, isBroadcastToAllRoomMembers: true);
        }

        /// <summary>
        /// 向房间内所有在线成员广播，但排除指定连接（通常是消息发送者自身）。
        /// 排除策略属于目标集合计算变体，不额外上升为新的框架级发送语义分类。
        /// 此方法不触发 Replay 旁路录制，因为投递范围不是全体房间成员。
        /// </summary>
        public void BroadcastToRoomExclude<TMessage>(string roomId, TMessage message, ConnectionId excludeConnectionId)
            where TMessage : S2CRoomMessage
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoomExclude 失败：roomId 为空，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoomExclude 失败：message 为 null，RoomId={roomId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoomExclude 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册，RoomId={roomId}。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] BroadcastToRoomExclude 失败：消息序列化结果为 null，RoomId={roomId}，MessageId={metadata.MessageId}。");
                return;
            }

            var allConnections = _roomManager.GetOnlineConnectionIds(roomId);
            if (allConnections == null || allConnections.Count == 0)
            {
                Debug.LogWarning($"[ServerRoomMessageSender] BroadcastToRoomExclude 跳过：房间 {roomId} 当前无在线成员，MessageId={metadata.MessageId}。");
                return;
            }

            // 构建排除后的目标集合，排除策略在发送器层完成，不进入 Coordinator
            var filteredConnections = new List<ConnectionId>(allConnections.Count);
            foreach (var connId in allConnections)
            {
                if (connId != excludeConnectionId)
                {
                    filteredConnections.Add(connId);
                }
            }

            if (filteredConnections.Count == 0)
            {
                Debug.LogWarning($"[ServerRoomMessageSender] BroadcastToRoomExclude 跳过：排除后目标集合为空，RoomId={roomId}，MessageId={metadata.MessageId}。");
                return;
            }

            // isBroadcastToAllRoomMembers=false，排除发送后不触发 Replay 旁路录制
            _coordinator.DispatchRoomBroadcast(filteredConnections, metadata.MessageId, payload, roomId, isBroadcastToAllRoomMembers: false);
        }

        /// <summary>
        /// 向房间内指定 SessionId 的成员单播房间域消息。
        /// 单播消息不触发 Replay 旁路录制，因为投递范围不是全体房间成员。
        /// </summary>
        public void SendToRoomMember<TMessage>(string roomId, string sessionId, TMessage message)
            where TMessage : S2CRoomMessage
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[ServerRoomMessageSender] SendToRoomMember 失败：roomId 为空，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[ServerRoomMessageSender] SendToRoomMember 失败：sessionId 为空，RoomId={roomId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            if (message == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] SendToRoomMember 失败：message 为 null，RoomId={roomId}，SessionId={sessionId}，消息类型={typeof(TMessage).Name}。");
                return;
            }

            var metadata = _messageRegistry.GetByType<TMessage>();
            if (metadata == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] SendToRoomMember 失败：消息类型 {typeof(TMessage).Name} 未在 MessageRegistry 中注册，RoomId={roomId}，SessionId={sessionId}。");
                return;
            }

            byte[] payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError($"[ServerRoomMessageSender] SendToRoomMember 失败：消息序列化结果为 null，RoomId={roomId}，SessionId={sessionId}，MessageId={metadata.MessageId}。");
                return;
            }

            var session = _sessionManager.GetSessionById(sessionId);
            if (session == null)
            {
                Debug.LogWarning($"[ServerRoomMessageSender] SendToRoomMember 跳过：SessionId={sessionId} 不存在，RoomId={roomId}，MessageId={metadata.MessageId}。");
                return;
            }

            if (!session.IsOnline)
            {
                Debug.LogWarning($"[ServerRoomMessageSender] SendToRoomMember 跳过：SessionId={sessionId} 当前不在线，RoomId={roomId}，MessageId={metadata.MessageId}。");
                return;
            }

            // 单播不触发 Replay 旁路录制
            _coordinator.DispatchToConnection(session.CurrentConnectionId, metadata.MessageId, payload, roomId);
        }
    }
}
