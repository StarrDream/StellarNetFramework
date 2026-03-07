// Assets/StellarNetFramework/Server/Network/Sender/ServerRoomMessageSender.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Shared.Protocol.Envelope;

namespace StellarNet.Server.Network.Sender
{
    // 服务端房间域消息发送器，只允许发送 S2CRoomMessage。
    // 支持全体房间成员广播与单体房间成员单播两种投递范围。
    // 发送时必须显式提供 roomId，不允许隐式读取任何运行时上下文。
    // 目标连接集解析由 ServerSendCoordinator 完成，本发送器只负责构建封套并转交。
    public sealed class ServerRoomMessageSender
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ServerSendCoordinator _sendCoordinator;

        public ServerRoomMessageSender(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ServerSendCoordinator sendCoordinator)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 初始化失败：serializer 不得为 null");
                return;
            }

            if (sendCoordinator == null)
            {
                Debug.LogError("[ServerRoomMessageSender] 初始化失败：sendCoordinator 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _sendCoordinator = sendCoordinator;
        }

        // 房间域广播：向指定房间内全体当前在线成员广播 S2CRoomMessage。
        // 典型场景：局内公共状态变化、公共表现事件、公共同步结果。
        // 此类消息满足录制条件，ServerSendCoordinator 会自动触发 Replay 旁路写入。
        // 参数 roomId：目标房间 ID，不得为空。
        // 参数 message：待发送的房间域下行消息，不得为 null。
        // 参数 onlineMemberConnections：目标房间当前在线成员连接集合，由调用方从 RoomInstance 获取后传入。
        public void BroadcastToRoom(
            string roomId,
            S2CRoomMessage message,
            IReadOnlyList<ConnectionId> onlineMemberConnections)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] BroadcastToRoom 失败：roomId 不得为空，" +
                    $"MessageType={message?.GetType().Name}");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] BroadcastToRoom 失败：message 不得为 null，" +
                    $"RoomId={roomId}");
                return;
            }

            if (onlineMemberConnections == null || onlineMemberConnections.Count == 0)
            {
                Debug.LogWarning(
                    $"[ServerRoomMessageSender] BroadcastToRoom 警告：目标连接集合为空，" +
                    $"RoomId={roomId}，MessageType={message.GetType().Name}，" +
                    $"广播目标集合为空时不视为一次有效公共广播发送，已跳过。");
                return;
            }

            var envelope = BuildEnvelope(message, roomId);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());

            // 转交 ServerSendCoordinator 完成广播投递与 Replay 旁路拦截
            _sendCoordinator.DispatchRoomBroadcast(
                roomId,
                envelope,
                meta.DeliveryMode,
                onlineMemberConnections,
                isPublicBroadcast: true);
        }

        // 房间域单播：向指定房间内单个成员发送 S2CRoomMessage。
        // 典型场景：个人私有提示、个人结算信息、仅个人可见的辅助通知。
        // 此类消息不参与 Replay 录制，ServerSendCoordinator 不触发旁路写入。
        // 参数 roomId：目标房间 ID，不得为空。
        // 参数 targetConnectionId：目标成员的框架统一连接标识。
        // 参数 message：待发送的房间域下行消息，不得为 null。
        public void SendToRoomMember(
            string roomId,
            ConnectionId targetConnectionId,
            S2CRoomMessage message)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] SendToRoomMember 失败：roomId 不得为空，" +
                    $"MessageType={message?.GetType().Name}，ConnectionId={targetConnectionId}");
                return;
            }

            if (!targetConnectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] SendToRoomMember 失败：targetConnectionId 无效，" +
                    $"RoomId={roomId}，MessageType={message?.GetType().Name}");
                return;
            }

            if (message == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] SendToRoomMember 失败：message 不得为 null，" +
                    $"RoomId={roomId}，ConnectionId={targetConnectionId}");
                return;
            }

            var envelope = BuildEnvelope(message, roomId);
            if (envelope == null)
                return;

            var meta = _messageRegistry.GetMetaByType(message.GetType());

            // 单播不触发 Replay 旁路写入
            _sendCoordinator.DispatchRoomBroadcast(
                roomId,
                envelope,
                meta.DeliveryMode,
                new List<ConnectionId> { targetConnectionId },
                isPublicBroadcast: false);
        }

        // 构建 NetworkEnvelope，完成序列化、MessageId 解析与 RoomId 绑定
        private NetworkEnvelope BuildEnvelope(S2CRoomMessage message, string roomId)
        {
            var meta = _messageRegistry.GetMetaByType(message.GetType());
            if (meta == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] BuildEnvelope 失败：" +
                    $"协议类型 {message.GetType().Name} 未在 MessageRegistry 中注册，" +
                    $"请检查本端程序集白名单配置。");
                return null;
            }

            var payload = _serializer.Serialize(message);
            if (payload == null)
            {
                Debug.LogError(
                    $"[ServerRoomMessageSender] BuildEnvelope 失败：序列化结果为 null，" +
                    $"MessageType={message.GetType().Name}，MessageId={meta.MessageId}，RoomId={roomId}");
                return null;
            }

            return new NetworkEnvelope(meta.MessageId, payload, roomId);
        }
    }
}
