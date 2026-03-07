// Assets/StellarNetFramework/Server/Network/Sender/ServerGlobalMessageSender.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Server.Session;

namespace StellarNet.Server.Network.Sender
{
    // 服务端全局域消息发送器，只允许发送 S2CGlobalMessage。
    // 支持全体客户端广播与单体客户端单播两种投递范围。
    // 发送器不负责目标连接集的房间语义展开，只负责全局域目标解析。
    // 所有发送请求最终交由 ServerSendCoordinator 完成上下文绑定与 Adapter 投递。
    public sealed class ServerGlobalMessageSender
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly SessionManager _sessionManager;
        private readonly ServerSendCoordinator _sendCoordinator;

        public ServerGlobalMessageSender(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            SessionManager sessionManager,
            ServerSendCoordinator sendCoordinator)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 初始化失败：serializer 不得为 null");
                return;
            }

            if (sessionManager == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (sendCoordinator == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] 初始化失败：sendCoordinator 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sessionManager = sessionManager;
            _sendCoordinator = sendCoordinator;
        }

        // 全局域单播：向单个目标连接发送 S2CGlobalMessage
        // 参数 targetConnectionId：目标客户端的框架统一连接标识
        // 参数 message：待发送的全局域下行消息，不得为 null
        public void SendToConnection(ConnectionId targetConnectionId, S2CGlobalMessage message)
        {
            if (!targetConnectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToConnection 失败：targetConnectionId 无效，" +
                    $"MessageType={message?.GetType().Name}");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToConnection 失败：message 不得为 null，" +
                    $"ConnectionId={targetConnectionId}");
                return;
            }

            var envelope = BuildEnvelope(message, roomId: string.Empty);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());
            _sendCoordinator.DispatchToConnection(targetConnectionId, envelope, meta.DeliveryMode);
        }

        // 全局域单播（通过 SessionId 寻址）：向指定会话的当前在线连接发送 S2CGlobalMessage
        // 目标会话离线时输出 Warning 并跳过，不视为错误
        public void SendToSession(SessionId targetSessionId, S2CGlobalMessage message)
        {
            if (!targetSessionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToSession 失败：targetSessionId 无效，" +
                    $"MessageType={message?.GetType().Name}");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToSession 失败：message 不得为 null，" +
                    $"SessionId={targetSessionId}");
                return;
            }

            var session = _sessionManager.GetSessionById(targetSessionId);
            if (session == null)
            {
                Debug.LogWarning(
                    $"[ServerGlobalMessageSender] SendToSession 警告：SessionId={targetSessionId} 不存在，" +
                    $"MessageType={message.GetType().Name}，发送已跳过。");
                return;
            }

            if (!session.IsOnline)
            {
                Debug.LogWarning(
                    $"[ServerGlobalMessageSender] SendToSession 警告：SessionId={targetSessionId} 当前离线，" +
                    $"MessageType={message.GetType().Name}，发送已跳过。");
                return;
            }

            SendToConnection(session.ConnectionId, message);
        }

        // 全局域广播：向所有当前在线客户端广播 S2CGlobalMessage
        // 典型场景：全局公告、全服提示、系统广播
        public void BroadcastToAll(S2CGlobalMessage message)
        {
            if (message == null)
            {
                Debug.LogError("[ServerGlobalMessageSender] BroadcastToAll 失败：message 不得为 null");
                return;
            }

            var envelope = BuildEnvelope(message, roomId: string.Empty);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());
            _sendCoordinator.DispatchToAll(envelope, meta.DeliveryMode);
        }

        // 全局域单播，携带目标房间参数（用于全局域消息语义需要目标房间参数的场景）
        // targetRoomId 仅表示该次发送语义中的目标房间参数，不将此消息提升为房间域消息
        public void SendToConnectionWithTargetRoom(
            ConnectionId targetConnectionId,
            S2CGlobalMessage message,
            string targetRoomId)
        {
            if (!targetConnectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToConnectionWithTargetRoom 失败：targetConnectionId 无效，" +
                    $"MessageType={message?.GetType().Name}，TargetRoomId={targetRoomId}");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] SendToConnectionWithTargetRoom 失败：message 不得为 null，" +
                    $"ConnectionId={targetConnectionId}，TargetRoomId={targetRoomId}");
                return;
            }

            // targetRoomId 作为全局域消息的目标房间参数写入封套，
            // 不触发房间归属一致性校验，不将此消息提升为房间域消息
            var envelope = BuildEnvelope(message, roomId: targetRoomId ?? string.Empty);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());
            _sendCoordinator.DispatchToConnection(targetConnectionId, envelope, meta.DeliveryMode);
        }

        // 构建 NetworkEnvelope，完成序列化与 MessageId 解析
        // 构建失败返回 null，由调用方决定是否继续
        private NetworkEnvelope BuildEnvelope(S2CGlobalMessage message, string roomId)
        {
            var meta = _messageRegistry.GetMetaByType(message.GetType());
            if (meta == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] BuildEnvelope 失败：" +
                    $"协议类型 {message.GetType().Name} 未在 MessageRegistry 中注册，" +
                    $"请检查本端程序集白名单配置。");
                return null;
            }

            var payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError(
                    $"[ServerGlobalMessageSender] BuildEnvelope 失败：序列化结果为 null，" +
                    $"MessageType={message.GetType().Name}，MessageId={meta.MessageId}");
                return null;
            }

            return new NetworkEnvelope(meta.MessageId, payload, roomId);
        }
    }
}
