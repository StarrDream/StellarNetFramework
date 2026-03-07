// Assets/StellarNetFramework/Server/Room/GlobalRoomManager.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Room.Assembler;
using StellarNet.Server.Room.Component;

namespace StellarNet.Server.Room
{
    // 服务端全局房间管理器，负责多房间集合维护、Tick 驱动、空置巡检与可路由集合管理。
    // 持有所有 RoomInstance 的生命周期控制权，是房间创建与销毁的唯一入口。
    // 向 ServerNetworkEntry 提供房间域路由委托，向 ServerSendCoordinator 提供 ReplayRecorder 查询委托。
    // 空置巡检与 Tick 驱动均由 GlobalInfrastructure.Tick() 统一触发，不自驱动。
    public sealed class GlobalRoomManager : IGlobalService
    {
        // 全部房间集合：RoomId → RoomInstance（含 Destroying/Destroyed 状态，等待清理）
        private readonly Dictionary<string, RoomInstance> _allRooms
            = new Dictionary<string, RoomInstance>();

        // 可路由房间集合：只包含 Running 状态的房间，供消息路由快速查找
        private readonly Dictionary<string, RoomInstance> _routableRooms
            = new Dictionary<string, RoomInstance>();

        // 房间域发送器，由 GlobalInfrastructure 在装配阶段注入，同时作为组件注入源
        private readonly ServerRoomMessageSender _roomSender;

        // 空置房间超时时长（毫秒），超过此时长无在线成员则触发销毁
        // 与 Session 保留超时独立配置、独立计时、独立生效
        private long _emptyRoomTimeoutMs;

        // 房间 ID 生成计数器
        private int _roomCounter = 0;

        public GlobalRoomManager(
            ServerRoomMessageSender roomSender,
            long emptyRoomTimeoutMs = 60000)
        {
            if (roomSender == null)
            {
                Debug.LogError("[GlobalRoomManager] 初始化失败：roomSender 不得为 null");
                return;
            }

            _roomSender = roomSender;
            _emptyRoomTimeoutMs = emptyRoomTimeoutMs;
        }

        // 更新空置房间超时时长，由 NetConfigManager 热重载时调用
        public void UpdateEmptyRoomTimeout(long timeoutMs)
        {
            _emptyRoomTimeoutMs = timeoutMs;
        }

        // ── 房间创建 ──────────────────────────────────────────────────────────

        // 创建并装配新房间。
        // 参数 components：业务组件列表，由调用方（RoomDispatcherModule）按业务需求构建。
        // 参数 nowUnixMs：当前时间戳（Unix 毫秒）。
        // 参数 replayRecorderWriter：可选的 ReplayRecorder 写入接口。
        // 返回装配成功的 RoomInstance，失败返回 null。
        public RoomInstance CreateRoom(
            IReadOnlyList<IServerRoomComponent> components,
            long nowUnixMs,
            IReplayRecorderWriter replayRecorderWriter = null)
        {
            if (components == null || components.Count == 0)
            {
                Debug.LogError("[GlobalRoomManager] CreateRoom 失败：组件列表为 null 或空");
                return null;
            }

            var roomId = GenerateRoomId(nowUnixMs);

            if (_allRooms.ContainsKey(roomId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] CreateRoom 失败：生成的 RoomId={roomId} 已存在，" +
                    $"请检查房间 ID 生成策略。");
                return null;
            }

            var room = new RoomInstance(roomId, nowUnixMs, _roomSender);
            _allRooms[roomId] = room;

            // _roomSender 同时作为框架层对象注入每个业务组件，
            // 组件不通过任何寻址机制自行获取发送器
            var success = ServerRoomAssembler.Assemble(
                room,
                components,
                _roomSender,
                replayRecorderWriter);

            if (!success)
            {
                Debug.LogError(
                    $"[GlobalRoomManager] CreateRoom 失败：RoomId={roomId} 装配失败，" +
                    $"已从房间集合中移除。");
                _allRooms.Remove(roomId);
                return null;
            }

            _routableRooms[roomId] = room;
            return room;
        }

        // ── 房间销毁 ──────────────────────────────────────────────────────────

        // 主动销毁指定房间，由业务逻辑（如结算完成、房主解散）触发。
        // 参数 onSessionRoomClear：销毁前通知外部清理会话房间归属的回调，参数为 RoomId。
        public void DestroyRoom(string roomId, Action<string> onSessionRoomClear = null)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[GlobalRoomManager] DestroyRoom 失败：roomId 不得为空");
                return;
            }

            if (!_allRooms.TryGetValue(roomId, out var room))
            {
                Debug.LogWarning(
                    $"[GlobalRoomManager] DestroyRoom 警告：RoomId={roomId} 不存在，本次销毁已忽略。");
                return;
            }

            if (room.LifecycleState == RoomLifecycleState.Destroying ||
                room.LifecycleState == RoomLifecycleState.Destroyed)
            {
                Debug.LogWarning(
                    $"[GlobalRoomManager] DestroyRoom 警告：RoomId={roomId} 已处于 " +
                    $"{room.LifecycleState} 状态，本次销毁已忽略。");
                return;
            }

            onSessionRoomClear?.Invoke(roomId);

            // 从可路由集合移除，阻断新消息路由到此房间
            _routableRooms.Remove(roomId);

            // 执行房间七步销毁流程
            room.Destroy();

            _allRooms.Remove(roomId);
        }

        // ── Tick 驱动 ─────────────────────────────────────────────────────────

        // 每帧驱动所有 Running 状态的房间，由 GlobalInfrastructure.Tick() 调用
        public void Tick(long deltaTimeMs, long nowUnixMs)
        {
            foreach (var kv in _routableRooms)
            {
                kv.Value.Tick(deltaTimeMs);
            }

            TickEmptyRoomCheck(nowUnixMs);
        }

        // 空置巡检：检测并销毁超时空置的房间
        private void TickEmptyRoomCheck(long nowUnixMs)
        {
            List<string> emptyRoomsToDestroy = null;

            foreach (var kv in _routableRooms)
            {
                var room = kv.Value;
                if (!room.IsEmpty)
                    continue;

                var emptyDuration = nowUnixMs - room.LastActiveUnixMs;
                if (emptyDuration >= _emptyRoomTimeoutMs)
                {
                    if (emptyRoomsToDestroy == null)
                        emptyRoomsToDestroy = new List<string>();

                    emptyRoomsToDestroy.Add(room.RoomId);
                }
            }

            if (emptyRoomsToDestroy == null)
                return;

            foreach (var roomId in emptyRoomsToDestroy)
            {
                Debug.LogWarning(
                    $"[GlobalRoomManager] 空置超时销毁：RoomId={roomId}，" +
                    $"超时阈值={_emptyRoomTimeoutMs}ms，已触发销毁流程。");

                _onEmptyRoomDestroy?.Invoke(roomId);
                DestroyRoom(roomId);
            }
        }

        // 空置超时销毁回调，由 GlobalInfrastructure 在装配阶段注入
        private Action<string> _onEmptyRoomDestroy;

        public void SetOnEmptyRoomDestroyCallback(Action<string> callback)
        {
            if (callback == null)
            {
                Debug.LogError(
                    "[GlobalRoomManager] SetOnEmptyRoomDestroyCallback 失败：callback 不得为 null");
                return;
            }

            _onEmptyRoomDestroy = callback;
        }

        // ── 路由委托 ──────────────────────────────────────────────────────────

        // 房间域路由委托实现，注入到 ServerNetworkEntry。
        // 通过 RoomId 查找可路由房间，转发给对应 RoomInstance 的 Router.Dispatch()。
        // 返回 true 表示成功路由；返回 false 表示路由失败（房间不存在或不可路由）。
        public bool RouteRoomMessage(
            ConnectionId connectionId,
            C2SRoomMessage message,
            string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] RouteRoomMessage 失败：roomId 不得为空，" +
                    $"ConnectionId={connectionId}，MessageType={message?.GetType().Name}");
                return false;
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] RouteRoomMessage 失败：RoomId={roomId} 不在可路由集合中，" +
                    $"ConnectionId={connectionId}，MessageType={message?.GetType().Name}，消息已丢弃。");
                return false;
            }

            room.Router.Dispatch(connectionId, message);
            return true;
        }

        // ReplayRecorder 查询委托实现，注入到 ServerSendCoordinator
        public IReplayRecorderWriter ResolveReplayRecorder(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                return null;

            if (!_routableRooms.TryGetValue(roomId, out var room))
                return null;

            return room.GetReplayRecorder();
        }

        // ── 查询 ──────────────────────────────────────────────────────────────

        // 通过 RoomId 获取 RoomInstance（含非 Running 状态），用于诊断与结算
        public RoomInstance GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                return null;

            _allRooms.TryGetValue(roomId, out var room);
            return room;
        }

        // 通过 RoomId 获取可路由 RoomInstance（仅 Running 状态）
        public RoomInstance GetRoutableRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
                return null;

            _routableRooms.TryGetValue(roomId, out var room);
            return room;
        }

        public int TotalRoomCount => _allRooms.Count;
        public int RoutableRoomCount => _routableRooms.Count;

        // ── 关停 ──────────────────────────────────────────────────────────────

        // 关停所有房间，由 GlobalInfrastructure.Shutdown() 调用
        public void ShutdownAll()
        {
            var roomIds = new List<string>(_routableRooms.Keys);
            foreach (var roomId in roomIds)
            {
                DestroyRoom(roomId);
            }

            _allRooms.Clear();
            _routableRooms.Clear();
        }

        // ── 内部工具 ──────────────────────────────────────────────────────────

        // 生成唯一房间 ID，格式：ROOM-{自增序号}-{时间戳后6位}
        private string GenerateRoomId(long nowUnixMs)
        {
            return $"ROOM-{_roomCounter++}-{nowUnixMs % 1000000}";
        }
    }
}