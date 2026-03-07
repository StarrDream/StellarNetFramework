// Assets/StellarNetFramework/Server/Network/Sender/ServerSendCoordinator.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Shared.Enums;
using StellarNet.Server.Network.Adapter;

namespace StellarNet.Server.Network.Sender
{
    // 服务端发送协调节点，是框架发送链中"目标连接集合解析完成后、进入 Adapter 前"的制度化节点。
    // 职责严格限定为：
    //   接收上层已确定协议类型的发送请求、完成目标连接集合解析、
    //   在满足录制条件时主动触发 Replay 旁路写入、将最终连接级发送任务交给 Adapter。
    // 明确不负责：业务状态修改、房间业务组件逻辑、协议语义解释。
    // Replay 旁路拦截必须由本节点主动发起，不得改为事件订阅监听方式。
    // 本节点通过 RoomInstance 挂载的 ReplayRecorder 获取录制写入目标，
    // 若当前房间未挂载有效 ReplayRecorder，则直接跳过录制分支，不影响正常发送流程。
    public sealed class ServerSendCoordinator
    {
        private readonly INetworkAdapter _adapter;

        // ReplayRecorder 查询委托：通过 roomId 获取当前房间挂载的 ReplayRecorder。
        // 由 GlobalRoomManager 在初始化后注入，避免直接持有 GlobalRoomManager 引用。
        // 若房间未启用录制或 ReplayRecorder 未挂载，委托应返回 null。
        private System.Func<string, IReplayRecorderWriter> _replayRecorderResolver;

        public ServerSendCoordinator(INetworkAdapter adapter)
        {
            if (adapter == null)
            {
                Debug.LogError("[ServerSendCoordinator] 初始化失败：adapter 不得为 null");
                return;
            }

            _adapter = adapter;
        }

        // 注入 ReplayRecorder 查询委托，由 GlobalRoomManager 初始化完成后调用
        public void SetReplayRecorderResolver(System.Func<string, IReplayRecorderWriter> resolver)
        {
            if (resolver == null)
            {
                Debug.LogError("[ServerSendCoordinator] SetReplayRecorderResolver 失败：resolver 不得为 null");
                return;
            }

            _replayRecorderResolver = resolver;
        }

        // 全局域连接级单播，直接投递到指定连接
        public void DispatchToConnection(
            ConnectionId connectionId,
            NetworkEnvelope envelope,
            DeliveryMode deliveryMode)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[ServerSendCoordinator] DispatchToConnection 失败：connectionId 无效，" +
                    $"MessageId={envelope?.MessageId}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError(
                    $"[ServerSendCoordinator] DispatchToConnection 失败：envelope 不得为 null，" +
                    $"ConnectionId={connectionId}");
                return;
            }

            _adapter.Send(connectionId, envelope, deliveryMode);
        }

        // 全局域全体广播，向所有在线连接投递
        // 在线连接集合由调用方（ServerGlobalMessageSender）从 SessionManager 获取后传入
        public void DispatchToAll(NetworkEnvelope envelope, DeliveryMode deliveryMode)
        {
            if (envelope == null)
            {
                Debug.LogError("[ServerSendCoordinator] DispatchToAll 失败：envelope 不得为 null");
                return;
            }

            // 全体广播委托，由 GlobalInfrastructure 注入，指向 Mirror NetworkServer.SendToAll
            if (_broadcastToAllDelegate == null)
            {
                Debug.LogError(
                    $"[ServerSendCoordinator] DispatchToAll 失败：全体广播委托未注入，" +
                    $"MessageId={envelope.MessageId}，请确认 GlobalInfrastructure 已完成装配。");
                return;
            }

            _broadcastToAllDelegate.Invoke(envelope, deliveryMode);
        }

        // 房间域广播/单播统一入口。
        // 参数 isPublicBroadcast：true 表示面向全体房间成员的公共广播，触发 Replay 旁路写入；
        //                         false 表示单体成员单播，不触发 Replay 录制。
        // 参数 targetConnections：已由上层完成解析的目标连接集合，本节点不再做房间语义展开。
        public void DispatchRoomBroadcast(
            string roomId,
            NetworkEnvelope envelope,
            DeliveryMode deliveryMode,
            IReadOnlyList<ConnectionId> targetConnections,
            bool isPublicBroadcast)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[ServerSendCoordinator] DispatchRoomBroadcast 失败：roomId 不得为空，" +
                    $"MessageId={envelope?.MessageId}");
                return;
            }

            if (envelope == null)
            {
                Debug.LogError(
                    $"[ServerSendCoordinator] DispatchRoomBroadcast 失败：envelope 不得为 null，" +
                    $"RoomId={roomId}");
                return;
            }

            if (targetConnections == null || targetConnections.Count == 0)
            {
                Debug.LogWarning(
                    $"[ServerSendCoordinator] DispatchRoomBroadcast 警告：目标连接集合为空，" +
                    $"RoomId={roomId}，MessageId={envelope.MessageId}，已跳过。");
                return;
            }

            // Replay 旁路拦截：仅对公共广播触发，由本节点主动发起，不通过事件订阅
            if (isPublicBroadcast)
            {
                TryWriteReplay(roomId, envelope);
            }

            // 逐连接投递，Adapter 只面向连接级发送
            foreach (var connectionId in targetConnections)
            {
                if (!connectionId.IsValid)
                {
                    Debug.LogWarning(
                        $"[ServerSendCoordinator] DispatchRoomBroadcast 警告：" +
                        $"目标连接集合中存在无效 ConnectionId，已跳过该连接，" +
                        $"RoomId={roomId}，MessageId={envelope.MessageId}");
                    continue;
                }

                _adapter.Send(connectionId, envelope, deliveryMode);
            }
        }

        // 注入全体广播委托，由 GlobalInfrastructure 在装配阶段注入
        // 指向 Mirror NetworkServer 的全体广播能力
        private System.Action<NetworkEnvelope, DeliveryMode> _broadcastToAllDelegate;

        public void SetBroadcastToAllDelegate(
            System.Action<NetworkEnvelope, DeliveryMode> broadcastDelegate)
        {
            if (broadcastDelegate == null)
            {
                Debug.LogError(
                    "[ServerSendCoordinator] SetBroadcastToAllDelegate 失败：委托不得为 null");
                return;
            }

            _broadcastToAllDelegate = broadcastDelegate;
        }

        // Replay 旁路写入，仅当满足以下全部条件时触发：
        //   1. 消息属于房间域（已由调用方保证）
        //   2. 消息方向为 S2C（已由调用方保证）
        //   3. 投递范围为全体房间成员公共广播（isPublicBroadcast = true）
        // 若当前房间未挂载有效 ReplayRecorder，直接跳过，不影响正常发送流程。
        private void TryWriteReplay(string roomId, NetworkEnvelope envelope)
        {
            if (_replayRecorderResolver == null)
                return;

            var recorder = _replayRecorderResolver.Invoke(roomId);
            if (recorder == null)
                return;

            recorder.WriteFrame(envelope);
        }
    }

    // Replay 录制写入接口，由 ReplayRecorder 实现，在 Batch 08 交付。
    // 此处前向声明，供 ServerSendCoordinator 在 Batch 06 阶段完成依赖引用，
    // 避免因批次顺序导致编译依赖断裂。
    public interface IReplayRecorderWriter
    {
        // 将一帧公共广播写入录制缓冲队列
        // 参数 envelope：已完成上下文绑定的 NetworkEnvelope，包含 MessageId、Payload 与 RoomId
        void WriteFrame(NetworkEnvelope envelope);
    }
}
