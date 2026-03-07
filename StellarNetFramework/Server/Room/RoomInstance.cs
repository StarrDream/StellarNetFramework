// Assets/StellarNetFramework/Server/Room/RoomInstance.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Room.RoomScope;
using StellarNet.Server.Room.EventBus;
using StellarNet.Server.Room.Component;

namespace StellarNet.Server.Room
{
    // 服务端房间实例，是单个房间的完整运行时沙盒。
    // 成员身份集合（_memberSessionIds）与在线连接映射（_onlineConnections）严格分离：
    //   成员身份集合：记录所有已加入此房间的会话 ID，不随连接断开而缩减。
    //   在线连接映射：记录当前持有有效连接的成员，断线时移除，重连时补充。
    // 两者分离是重连机制与回放机制的基础，不得合并为单一集合。
    // 生命周期状态机：Initializing → Running → Destroying → Destroyed。
    // 销毁顺序约束（必须严格遵守）：
    //   第一步：标记状态为 Destroying，阻断新消息进入
    //   第二步：按逆序调用所有组件的 OnDestroy()
    //   第三步：清空 RoomEventBus 全部订阅
    //   第四步：清空 RoomServiceLocator 全部注册
    //   第五步：清空 ServerRoomMessageRouter 全部 Handler
    //   第六步：清空成员身份集合与在线连接映射
    //   第七步：标记状态为 Destroyed
    public sealed class RoomInstance
    {
        // 房间唯一标识
        public string RoomId { get; }

        // 当前房间生命周期状态
        public RoomLifecycleState LifecycleState { get; private set; }

        // 房间创建时间戳（Unix 毫秒）
        public long CreatedUnixMs { get; }

        // 房间最后活跃时间戳（Unix 毫秒），用于空置巡检
        public long LastActiveUnixMs { get; private set; }

        // 房间内部基础设施
        public ServerRoomMessageRouter Router { get; }
        public RoomServiceLocator ServiceLocator { get; }
        public RoomEventBus EventBus { get; }

        // 房间域发送器，由 GlobalRoomManager 在创建时注入
        private readonly ServerRoomMessageSender _roomSender;

        // 成员身份集合：所有已加入此房间的 SessionId，不随连接断开而缩减
        private readonly HashSet<string> _memberSessionIds = new HashSet<string>();

        // 在线连接映射：SessionId → ConnectionId，只记录当前持有有效连接的成员
        private readonly Dictionary<string, ConnectionId> _onlineConnections
            = new Dictionary<string, ConnectionId>();

        // 连接反向索引：ConnectionId → SessionId，用于收包时快速定位成员
        private readonly Dictionary<ConnectionId, string> _connectionToSession
            = new Dictionary<ConnectionId, string>();

        // 已装配的组件列表，按装配顺序排列，销毁时逆序
        private readonly List<IServerRoomComponent> _components = new List<IServerRoomComponent>();

        // ReplayRecorder 写入接口，由 ServerRoomAssembler 在装配阶段挂载
        // 未启用录制时为 null，ServerSendCoordinator 检测到 null 时直接跳过录制分支
        private IReplayRecorderWriter _replayRecorder;

        public RoomInstance(
            string roomId,
            long createdUnixMs,
            ServerRoomMessageSender roomSender)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[RoomInstance] 构造失败：roomId 不得为空");
                return;
            }

            if (roomSender == null)
            {
                Debug.LogError($"[RoomInstance] 构造失败：roomSender 不得为 null，RoomId={roomId}");
                return;
            }

            RoomId = roomId;
            CreatedUnixMs = createdUnixMs;
            LastActiveUnixMs = createdUnixMs;
            LifecycleState = RoomLifecycleState.Initializing;

            Router = new ServerRoomMessageRouter(roomId);
            ServiceLocator = new RoomServiceLocator(roomId);
            EventBus = new RoomEventBus(roomId);
            _roomSender = roomSender;
        }

        // ── 生命周期 ──────────────────────────────────────────────────────────

        // 标记房间进入 Running 状态，由 ServerRoomAssembler 装配成功后调用
        public void MarkRunning()
        {
            if (LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[RoomInstance] MarkRunning 失败：当前状态 {LifecycleState} 不允许转入 Running，" +
                    $"RoomId={RoomId}");
                return;
            }

            LifecycleState = RoomLifecycleState.Running;
        }

        // 每帧驱动，由 GlobalRoomManager.Tick() 调用
        // Destroying/Destroyed 状态下直接跳过，不驱动任何组件
        public void Tick(long deltaTimeMs)
        {
            if (LifecycleState != RoomLifecycleState.Running)
                return;

            // 重置 EventBus 帧计数器，保证 Warning 阈值统计以帧为单位
            EventBus.ResetFrameCounter();

            foreach (var component in _components)
            {
                component.Tick(deltaTimeMs);
            }
        }

        // 执行完整销毁流程，严格遵循七步销毁顺序约束
        // 由 GlobalRoomManager 在确认房间可销毁后调用
        public void Destroy()
        {
            if (LifecycleState == RoomLifecycleState.Destroying ||
                LifecycleState == RoomLifecycleState.Destroyed)
            {
                Debug.LogWarning(
                    $"[RoomInstance] Destroy 重复调用警告：当前状态已为 {LifecycleState}，" +
                    $"RoomId={RoomId}，本次调用已忽略。");
                return;
            }

            // 第一步：标记状态为 Destroying，阻断新消息进入
            LifecycleState = RoomLifecycleState.Destroying;

            // 第二步：按逆序调用所有组件的 OnDestroy()
            for (var i = _components.Count - 1; i >= 0; i--)
            {
                var component = _components[i];
                component.OnDestroy();
            }

            // 第三步：清空 RoomEventBus 全部订阅
            EventBus.Clear();

            // 第四步：清空 RoomServiceLocator 全部注册
            ServiceLocator.Clear();

            // 第五步：清空 ServerRoomMessageRouter 全部 Handler
            Router.Clear();

            // 第六步：清空成员身份集合与在线连接映射
            _memberSessionIds.Clear();
            _onlineConnections.Clear();
            _connectionToSession.Clear();
            _components.Clear();

            // 第七步：标记状态为 Destroyed
            LifecycleState = RoomLifecycleState.Destroyed;
        }

        // ── 组件管理 ──────────────────────────────────────────────────────────

        // 注册已装配成功的组件到组件列表，由 ServerRoomAssembler 在 Init 成功后调用
        // 外部不得直接调用此方法
        internal void AddComponent(IServerRoomComponent component)
        {
            if (component == null)
            {
                Debug.LogError($"[RoomInstance] AddComponent 失败：component 不得为 null，RoomId={RoomId}");
                return;
            }

            _components.Add(component);
        }

        // 挂载 ReplayRecorder，由 ServerRoomAssembler 在装配阶段调用
        public void SetReplayRecorder(IReplayRecorderWriter recorder)
        {
            _replayRecorder = recorder;
        }

        // 获取当前挂载的 ReplayRecorder，供 ServerSendCoordinator 查询
        public IReplayRecorderWriter GetReplayRecorder()
        {
            return _replayRecorder;
        }

        // ── 成员管理 ──────────────────────────────────────────────────────────

        // 添加成员到身份集合，由 RoomDispatcherModule 在加房成功后调用
        // 不自动建立在线连接映射，在线连接映射由 SetMemberOnline 单独建立
        public void AddMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[RoomInstance] AddMember 失败：sessionId 不得为空，RoomId={RoomId}");
                return;
            }

            if (LifecycleState != RoomLifecycleState.Running &&
                LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[RoomInstance] AddMember 失败：当前房间状态 {LifecycleState} 不允许加入新成员，" +
                    $"RoomId={RoomId}，SessionId={sessionId}");
                return;
            }

            if (_memberSessionIds.Contains(sessionId))
            {
                Debug.LogWarning(
                    $"[RoomInstance] AddMember 警告：SessionId={sessionId} 已在成员身份集合中，" +
                    $"RoomId={RoomId}，本次添加已忽略。");
                return;
            }

            _memberSessionIds.Add(sessionId);
            LastActiveUnixMs = GetCurrentUnixMs();
        }

        // 从成员身份集合移除成员，由 RoomDispatcherModule 在离房或踢出时调用
        // 同时清理对应的在线连接映射
        public void RemoveMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[RoomInstance] RemoveMember 失败：sessionId 不得为空，RoomId={RoomId}");
                return;
            }

            _memberSessionIds.Remove(sessionId);

            // 同步清理在线连接映射
            if (_onlineConnections.TryGetValue(sessionId, out var connectionId))
            {
                _onlineConnections.Remove(sessionId);
                _connectionToSession.Remove(connectionId);
            }
        }

        // 建立成员在线连接映射，由 SessionManager 连接接管完成后调用
        // 若该成员不在成员身份集合中，直接报错阻断
        public void SetMemberOnline(string sessionId, ConnectionId connectionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[RoomInstance] SetMemberOnline 失败：sessionId 不得为空，" +
                    $"RoomId={RoomId}，ConnectionId={connectionId}");
                return;
            }

            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[RoomInstance] SetMemberOnline 失败：connectionId 无效，" +
                    $"RoomId={RoomId}，SessionId={sessionId}");
                return;
            }

            if (!_memberSessionIds.Contains(sessionId))
            {
                Debug.LogError(
                    $"[RoomInstance] SetMemberOnline 失败：SessionId={sessionId} 不在成员身份集合中，" +
                    $"RoomId={RoomId}，不允许为非成员建立在线连接映射。");
                return;
            }

            // 若该成员已有旧连接映射，先清理旧连接反向索引
            if (_onlineConnections.TryGetValue(sessionId, out var oldConnectionId))
            {
                _connectionToSession.Remove(oldConnectionId);
            }

            _onlineConnections[sessionId] = connectionId;
            _connectionToSession[connectionId] = sessionId;
            LastActiveUnixMs = GetCurrentUnixMs();
        }

        // 清除成员在线连接映射，由连接断开事件触发
        // 不从成员身份集合移除，保留成员身份等待重连或超时
        public void SetMemberOffline(ConnectionId connectionId)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[RoomInstance] SetMemberOffline 失败：connectionId 无效，RoomId={RoomId}");
                return;
            }

            if (!_connectionToSession.TryGetValue(connectionId, out var sessionId))
                return;

            _connectionToSession.Remove(connectionId);
            _onlineConnections.Remove(sessionId);
        }

        // ── 查询 ──────────────────────────────────────────────────────────────

        // 判断指定 SessionId 是否在成员身份集合中（不论是否在线）
        public bool IsMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;
            return _memberSessionIds.Contains(sessionId);
        }

        // 判断指定 SessionId 是否当前在线（持有有效连接）
        public bool IsMemberOnline(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return false;
            return _onlineConnections.ContainsKey(sessionId);
        }

        // 通过 ConnectionId 查找对应的 SessionId
        public bool TryGetSessionByConnection(ConnectionId connectionId, out string sessionId)
        {
            return _connectionToSession.TryGetValue(connectionId, out sessionId);
        }

        // 获取当前全体在线成员的 ConnectionId 列表，用于房间域广播目标集合构建
        // 返回只读快照，不暴露内部集合引用
        public IReadOnlyList<ConnectionId> GetOnlineMemberConnections()
        {
            var result = new List<ConnectionId>(_onlineConnections.Count);
            foreach (var kv in _onlineConnections)
            {
                result.Add(kv.Value);
            }
            return result;
        }

        // 获取全体成员 SessionId 快照（含离线成员），用于回放录制与结算
        public IReadOnlyList<string> GetAllMemberSessionIds()
        {
            var result = new List<string>(_memberSessionIds);
            return result;
        }

        // 当前总成员数（含离线）
        public int MemberCount => _memberSessionIds.Count;

        // 当前在线成员数
        public int OnlineMemberCount => _onlineConnections.Count;

        // 判断房间是否处于空置状态（无任何在线成员）
        public bool IsEmpty => _onlineConnections.Count == 0;

        // 判断房间是否处于可路由状态（Running 且非 Destroying/Destroyed）
        public bool IsRoutable => LifecycleState == RoomLifecycleState.Running;

        // ── 内部工具 ──────────────────────────────────────────────────────────

        // 获取当前 Unix 毫秒时间戳，仅用于 LastActiveUnixMs 更新
        // 正式时间源由 GlobalRoomManager.Tick() 统一传入，此处仅作为成员操作时的辅助更新
        private long GetCurrentUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // 房间生命周期状态枚举
    public enum RoomLifecycleState
    {
        // 装配阶段，组件正在初始化，尚未开放消息路由
        Initializing = 0,

        // 正常运行阶段，开放消息路由与 Tick 驱动
        Running = 1,

        // 销毁中，已阻断新消息进入，正在执行七步销毁流程
        Destroying = 2,

        // 已完全销毁，GlobalRoomManager 可安全从集合中移除
        Destroyed = 3
    }
}
