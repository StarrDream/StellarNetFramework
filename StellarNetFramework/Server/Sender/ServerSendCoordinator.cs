using System.Collections.Generic;
using StellarNet.Server.Adapter;
using StellarNet.Server.Replay;
using StellarNet.Server.Room;
using StellarNet.Shared.Envelope;
using StellarNet.Shared.Identity;
using UnityEngine;

namespace StellarNet.Server.Sender
{
    /// <summary>
    /// 服务端发送协调节点，位于"目标连接集合解析完成后、进入 Adapter 前"的发送管线关键节点。
    /// 职责严格限定为：
    ///   1. 接收上层已确定协议类型的发送请求
    ///   2. 完成 NetworkEnvelope 运行时上下文绑定
    ///   3. 在满足录制条件时主动触发 Replay 自动旁路写入（主动调用，非事件订阅）
    ///   4. 将最终连接级发送任务交给 Adapter
    /// 不负责业务状态修改，不是 Adapter 的职责扩展，不是房间业务组件的一部分。
    /// Replay 旁路拦截必须由此节点主动发起，不得改为事件订阅监听方式。
    /// 通过当前 RoomInstance 挂载的 ReplayRecorder 获取录制写入目标。
    /// 若当前房间未挂载有效 ReplayRecorder，直接跳过录制分支，不影响正常发送流程。
    /// </summary>
    public sealed class ServerSendCoordinator
    {
        private readonly MirrorServerAdapter _adapter;
        private readonly GlobalRoomManager _roomManager;

        public ServerSendCoordinator(MirrorServerAdapter adapter, GlobalRoomManager roomManager)
        {
            if (adapter == null)
            {
                Debug.LogError("[ServerSendCoordinator] 构造失败：adapter 为 null。");
                return;
            }
            if (roomManager == null)
            {
                Debug.LogError("[ServerSendCoordinator] 构造失败：roomManager 为 null。");
                return;
            }

            _adapter = adapter;
            _roomManager = roomManager;
        }

        /// <summary>
        /// 向单个连接投递消息（全局域单播或房间域单播）。
        /// 单播消息不触发 Replay 旁路录制，因为录制只针对房间域全体广播。
        /// </summary>
        public void DispatchToConnection(ConnectionId connectionId, int messageId, byte[] payload, string roomId)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError($"[ServerSendCoordinator] DispatchToConnection 失败：ConnectionId 无效，MessageId={messageId}，RoomId={roomId}。");
                return;
            }

            if (payload == null || payload.Length == 0)
            {
                Debug.LogError($"[ServerSendCoordinator] DispatchToConnection 失败：payload 为 null 或空，ConnectionId={connectionId}，MessageId={messageId}。");
                return;
            }

            var envelope = new NetworkEnvelope(messageId, payload, roomId ?? string.Empty);
            _adapter.Send(connectionId, envelope);
        }

        /// <summary>
        /// 向多个连接批量投递消息（全局广播）。
        /// 全局广播不触发 Replay 旁路录制。
        /// </summary>
        public void DispatchToConnections(IReadOnlyList<ConnectionId> connectionIds, int messageId, byte[] payload, string roomId)
        {
            if (connectionIds == null || connectionIds.Count == 0)
            {
                Debug.LogWarning($"[ServerSendCoordinator] DispatchToConnections 跳过：目标连接集合为空，MessageId={messageId}。");
                return;
            }

            if (payload == null || payload.Length == 0)
            {
                Debug.LogError($"[ServerSendCoordinator] DispatchToConnections 失败：payload 为 null 或空，MessageId={messageId}。");
                return;
            }

            // 构建一次 Envelope，复用同一 payload 字节数组，避免重复序列化
            var envelope = new NetworkEnvelope(messageId, payload, roomId ?? string.Empty);
            foreach (var connId in connectionIds)
            {
                if (!connId.IsValid)
                {
                    Debug.LogWarning($"[ServerSendCoordinator] DispatchToConnections 跳过无效 ConnectionId={connId}，MessageId={messageId}。");
                    continue;
                }
                _adapter.Send(connId, envelope);
            }
        }

        /// <summary>
        /// 向房间成员集合投递房间域消息，并在满足条件时主动触发 Replay 旁路写入。
        /// 此方法是 Replay 旁路拦截的唯一触发点，必须由此节点主动调用，不得改为事件订阅。
        /// 满足录制条件：
        ///   1. isBroadcastToAllRoomMembers = true（面向全体房间成员的公共广播）
        ///   2. 当前房间业务主生命周期处于"游戏中"阶段
        ///   3. 当前房间挂载了有效 ReplayRecorder
        /// 任一条件不满足则跳过录制分支，不影响正常发送流程。
        /// </summary>
        public void DispatchRoomBroadcast(
            IReadOnlyList<ConnectionId> connectionIds,
            int messageId,
            byte[] payload,
            string roomId,
            bool isBroadcastToAllRoomMembers)
        {
            if (connectionIds == null || connectionIds.Count == 0)
            {
                Debug.LogWarning($"[ServerSendCoordinator] DispatchRoomBroadcast 跳过：目标连接集合为空，RoomId={roomId}，MessageId={messageId}。");
                return;
            }

            if (payload == null || payload.Length == 0)
            {
                Debug.LogError($"[ServerSendCoordinator] DispatchRoomBroadcast 失败：payload 为 null 或空，RoomId={roomId}，MessageId={messageId}。");
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[ServerSendCoordinator] DispatchRoomBroadcast 失败：roomId 为空，MessageId={messageId}。");
                return;
            }

            var envelope = new NetworkEnvelope(messageId, payload, roomId);

            // Replay 旁路拦截：在进入 Adapter 前主动检查录制条件并写入
            // 必须由此节点主动发起，不得改为事件订阅监听方式
            if (isBroadcastToAllRoomMembers)
            {
                TryWriteReplayRecord(roomId, messageId, payload);
            }

            // 完成正常发送流程，旁路拦截不影响主发送链
            foreach (var connId in connectionIds)
            {
                if (!connId.IsValid)
                {
                    Debug.LogWarning($"[ServerSendCoordinator] DispatchRoomBroadcast 跳过无效 ConnectionId={connId}，RoomId={roomId}，MessageId={messageId}。");
                    continue;
                }
                _adapter.Send(connId, envelope);
            }
        }

        /// <summary>
        /// 尝试向当前房间的 ReplayRecorder 写入录制记录。
        /// 通过 GlobalRoomManager 获取目标 RoomInstance 挂载的 ReplayRecorder。
        /// 若当前房间未挂载有效 ReplayRecorder 或房间不处于"游戏中"阶段，直接跳过，不影响正常发送流程。
        /// </summary>
        private void TryWriteReplayRecord(string roomId, int messageId, byte[] payload)
        {
            // 通过 GlobalRoomManager 代理查询房间录制器，全局模块不得直接穿透房间内部
            var recorder = _roomManager.GetReplayRecorder(roomId);
            if (recorder == null)
            {
                // 房间未启用录制，跳过录制分支，属于正常情况
                return;
            }

            // 录制条件由 ReplayRecorder 自身判断：是否处于"游戏中"阶段
            // 此处只负责触发写入，不重复判断业务阶段
            recorder.TryWrite(messageId, payload, roomId);
        }
    }
}
