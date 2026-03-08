using System.Collections.Generic;
using StellarNet.Server.Network;
using StellarNet.Server.Replay;
using StellarNet.Server.ServiceLocator;
using StellarNet.Shared.Identity;
using StellarNet.Shared.RoomSettings;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 单个房间的唯一生命周期总入口，是房间域的唯一生命周期源。
    /// 房间业务组件不拥有独立主生命周期，其生命周期完全来自此实例的统一驱动。
    /// 严格区分：成员身份集合（逻辑归属）与当前在线连接映射（物理在线状态）。
    /// 断线后成员仍可属于房间，但可不在当前在线连接映射中。
    /// 广播目标计算默认基于当前在线连接映射，不基于纯成员身份集合。
    /// 一旦进入 Destroying 状态，立即从可路由集合中移除，后续重复销毁请求只输出 Warning 并返回。
    /// </summary>
    public sealed class RoomInstance
    {
        // ── 基础标识 ─────────────────────────────────────────────────

        /// <summary>
        /// 房间唯一标识，是房间隔离、消息路由、重连恢复、录制归档的关键上下文标识。
        /// </summary>
        public string RoomId { get; }

        // ── 生命周期状态 ──────────────────────────────────────────────

        /// <summary>
        /// 当前框架对象运行态，只用于框架对象管理，不等于房间业务主生命周期。
        /// </summary>
        public RoomLifecycleState LifecycleState { get; private set; }

        /// <summary>
        /// 当前房间业务主生命周期阶段，录制边界、重连恢复边界均挂靠此字段。
        /// </summary>
        public RoomBusinessPhase BusinessPhase { get; private set; }

        // ── 成员管理（严格分离两类集合）─────────────────────────────

        /// <summary>
        /// 成员身份集合：表示哪些 SessionId 逻辑上属于该房间。
        /// 断线后成员仍可保留其成员归属，房间成员归属判断针对此集合。
        /// </summary>
        private readonly HashSet<string> _memberSessionIds = new HashSet<string>();

        /// <summary>
        /// 当前在线连接映射：SessionId → ConnectionId，表示当前有哪些有效连接在线附着。
        /// 重连接管时可发生替换，房间空置超时判断针对此映射是否为空。
        /// 广播目标计算默认基于此映射，不基于纯成员身份集合。
        /// </summary>
        private readonly Dictionary<string, ConnectionId> _onlineConnections
            = new Dictionary<string, ConnectionId>();

        // ── 框架基础设施 ──────────────────────────────────────────────

        /// <summary>
        /// 房间作用域服务定位器，只用于单个房间作用域内服务寻址。
        /// </summary>
        public RoomScopeServiceLocator RoomServiceLocator { get; }

        /// <summary>
        /// 房间域事件总线，负责房间内业务组件之间的领域事件解耦传播。
        /// </summary>
        public RoomEventBus EventBus { get; }

        /// <summary>
        /// 房间内消息路由器，在房间创建时即存在，内部 Handle 注册表通过装配器动态注册。
        /// </summary>
        public ServerRoomMessageRouter MessageRouter { get; }

        /// <summary>
        /// 房间配置快照，在构造阶段就必须持有，不允许先创建不完整房间后再补挂配置。
        /// </summary>
        public IRoomSettings Settings { get; }

        /// <summary>
        /// 回放录制器挂载位，只有当前房间策略启用录制时才挂载有效实例。
        /// 未启用录制时此字段为 null，发送协调节点发现为 null 时直接跳过录制分支。
        /// </summary>
        public ReplayRecorder ReplayRecorder { get; private set; }

        // ── 房间内 Tick 计数器 ────────────────────────────────────────

        /// <summary>
        /// 房间内相对 Tick 计数，从 0 开始，每次 Tick 递增。
        /// 录制文件中的 Tick 表示此相对 Tick，不是服务端全局 Tick。
        /// </summary>
        public int CurrentTick { get; private set; }

        // ── 房间业务组件列表（由装配器写入）─────────────────────────

        /// <summary>
        /// 已装配的房间业务组件列表，由 ServerRoomAssembler 在装配阶段写入。
        /// 装配顺序即组件列表顺序，生命周期回调按此顺序分发。
        /// </summary>
        private readonly List<IRoomComponent> _components = new List<IRoomComponent>();

        // ── 空置超时计时 ──────────────────────────────────────────────

        private float _emptyStartTime = -1f;

        public RoomInstance(string roomId, IRoomSettings settings)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[RoomInstance] 构造失败：roomId 为空，房间无法正常工作。");
                return;
            }

            if (settings == null)
            {
                Debug.LogError($"[RoomInstance] 构造失败：settings 为 null，RoomId={roomId}，房间必须在构造阶段持有有效 IRoomSettings。");
                return;
            }

            RoomId = roomId;
            Settings = settings;
            LifecycleState = RoomLifecycleState.Initializing;
            BusinessPhase = RoomBusinessPhase.Created;
            CurrentTick = 0;

            // 房间基础设施在构造时即创建，不依赖装配器
            RoomServiceLocator = new RoomScopeServiceLocator(roomId);
            EventBus = new RoomEventBus(roomId);
            MessageRouter = new ServerRoomMessageRouter(roomId);
        }

        // ── 装配器专用接口 ────────────────────────────────────────────

        /// <summary>
        /// 由 ServerRoomAssembler 在装配完成后调用，将房间状态从 Initializing 推进到 Running。
        /// 装配失败时不得调用此方法。
        /// </summary>
        public void MarkRunning()
        {
            if (LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[RoomInstance] MarkRunning 失败：当前状态不是 Initializing，实际状态={LifecycleState}，RoomId={RoomId}。");
                return;
            }

            LifecycleState = RoomLifecycleState.Running;
        }

        /// <summary>
        /// 由 ServerRoomAssembler 在装配阶段写入已装配的业务组件。
        /// 只允许在 Initializing 阶段调用。
        /// </summary>
        public void AddComponent(IRoomComponent component)
        {
            if (LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[RoomInstance] AddComponent 失败：只允许在 Initializing 阶段添加组件，当前状态={LifecycleState}，RoomId={RoomId}。");
                return;
            }

            if (component == null)
            {
                Debug.LogError($"[RoomInstance] AddComponent 失败：component 为 null，RoomId={RoomId}。");
                return;
            }

            _components.Add(component);
        }

        /// <summary>
        /// 由 ServerRoomAssembler 在装配回滚时移除已添加的组件。
        /// </summary>
        public void RemoveComponent(IRoomComponent component)
        {
            _components.Remove(component);
        }

        /// <summary>
        /// 挂载回放录制器，只有当前房间策略启用录制时才调用。
        /// 只允许在 Initializing 阶段挂载。
        /// </summary>
        public void MountReplayRecorder(ReplayRecorder recorder)
        {
            if (LifecycleState != RoomLifecycleState.Initializing)
            {
                Debug.LogError(
                    $"[RoomInstance] MountReplayRecorder 失败：只允许在 Initializing 阶段挂载，当前状态={LifecycleState}，RoomId={RoomId}。");
                return;
            }

            if (recorder == null)
            {
                Debug.LogError($"[RoomInstance] MountReplayRecorder 失败：recorder 为 null，RoomId={RoomId}。");
                return;
            }

            ReplayRecorder = recorder;
        }

        // ── 成员管理接口 ──────────────────────────────────────────────

        /// <summary>
        /// 将 SessionId 加入成员身份集合，并建立在线连接映射。
        /// 成员身份集合与在线连接映射分别维护，不得混用。
        /// </summary>
        public void AddMember(string sessionId, ConnectionId connectionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[RoomInstance] AddMember 失败：sessionId 为空，RoomId={RoomId}，ConnectionId={connectionId}。");
                return;
            }

            _memberSessionIds.Add(sessionId);

            if (connectionId.IsValid)
            {
                _onlineConnections[sessionId] = connectionId;
            }
        }

        /// <summary>
        /// 将 SessionId 从成员身份集合与在线连接映射中同时移除。
        /// 用于成员主动离开或被踢出的场景。
        /// </summary>
        public void RemoveMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            _memberSessionIds.Remove(sessionId);
            _onlineConnections.Remove(sessionId);
        }

        /// <summary>
        /// 更新指定成员的在线连接映射，用于重连接管时替换旧连接。
        /// 只更新在线连接映射，不影响成员身份集合。
        /// </summary>
        public void UpdateMemberConnection(string sessionId, ConnectionId newConnectionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[RoomInstance] UpdateMemberConnection 失败：sessionId 为空，RoomId={RoomId}。");
                return;
            }

            if (!_memberSessionIds.Contains(sessionId))
            {
                Debug.LogError(
                    $"[RoomInstance] UpdateMemberConnection 失败：SessionId={sessionId} 不在成员身份集合中，RoomId={RoomId}。");
                return;
            }

            if (newConnectionId.IsValid)
            {
                _onlineConnections[sessionId] = newConnectionId;
            }
            else
            {
                _onlineConnections.Remove(sessionId);
            }
        }

        /// <summary>
        /// 将指定成员标记为断线，从在线连接映射中移除，但保留其成员身份。
        /// 断线后成员仍属于房间，但不在当前在线连接映射中。
        /// </summary>
        public void MarkMemberOffline(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            _onlineConnections.Remove(sessionId);
        }

        /// <summary>
        /// 获取当前所有在线成员的 ConnectionId 列表，用于广播目标集合计算。
        /// 广播目标计算默认基于在线连接映射，不基于纯成员身份集合。
        /// </summary>
        public List<ConnectionId> GetOnlineConnectionIds()
        {
            var result = new List<ConnectionId>(_onlineConnections.Count);
            foreach (var kv in _onlineConnections)
            {
                if (kv.Value.IsValid)
                {
                    result.Add(kv.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// 判断当前房间是否所有成员均已断线（在线连接映射为空）。
        /// 房间空置超时判断针对此方法，而不是成员身份集合是否为空。
        /// </summary>
        public bool IsAllMembersOffline()
        {
            return _onlineConnections.Count == 0;
        }

        /// <summary>
        /// 判断指定 SessionId 是否属于此房间的成员（成员身份集合判断）。
        /// </summary>
        public bool IsMember(string sessionId)
        {
            return !string.IsNullOrEmpty(sessionId) && _memberSessionIds.Contains(sessionId);
        }

        /// <summary>
        /// 获取当前成员数量（成员身份集合）。
        /// </summary>
        public int MemberCount => _memberSessionIds.Count;

        // ── 业务阶段推进接口 ──────────────────────────────────────────

        /// <summary>
        /// 推进房间业务阶段到等待开始，由房间调度模块在房间创建完成后调用。
        /// </summary>
        public void AdvanceToWaitingForStart()
        {
            if (BusinessPhase != RoomBusinessPhase.Created)
            {
                Debug.LogError(
                    $"[RoomInstance] AdvanceToWaitingForStart 失败：当前业务阶段不是 Created，实际={BusinessPhase}，RoomId={RoomId}。");
                return;
            }

            BusinessPhase = RoomBusinessPhase.WaitingForStart;
            DispatchOnRoomWaitStart();
        }

        /// <summary>
        /// 推进房间业务阶段到游戏中，由房间基础设置组件在满足开始条件时调用。
        /// 录制从此阶段开始生效。
        /// </summary>
        public void AdvanceToInGame()
        {
            if (BusinessPhase != RoomBusinessPhase.WaitingForStart)
            {
                Debug.LogError(
                    $"[RoomInstance] AdvanceToInGame 失败：当前业务阶段不是 WaitingForStart，实际={BusinessPhase}，RoomId={RoomId}。");
                return;
            }

            BusinessPhase = RoomBusinessPhase.InGame;

            // 通知录制器游戏正式开始，录制从此刻启用
            ReplayRecorder?.OnGameStarted();
            DispatchOnRoomStartGame();
        }

        /// <summary>
        /// 推进房间业务阶段到游戏结束处理中。
        /// </summary>
        public void AdvanceToGameEnding()
        {
            if (BusinessPhase != RoomBusinessPhase.InGame)
            {
                Debug.LogError(
                    $"[RoomInstance] AdvanceToGameEnding 失败：当前业务阶段不是 InGame，实际={BusinessPhase}，RoomId={RoomId}。");
                return;
            }

            BusinessPhase = RoomBusinessPhase.GameEnding;

            // 通知录制器游戏已结束，停止录制
            ReplayRecorder?.OnGameEnded();
            DispatchOnRoomGameEnding();
        }

        /// <summary>
        /// 推进房间业务阶段到结算中。
        /// </summary>
        public void AdvanceToSettling()
        {
            if (BusinessPhase != RoomBusinessPhase.GameEnding)
            {
                Debug.LogError(
                    $"[RoomInstance] AdvanceToSettling 失败：当前业务阶段不是 GameEnding，实际={BusinessPhase}，RoomId={RoomId}。");
                return;
            }

            BusinessPhase = RoomBusinessPhase.Settling;
            DispatchOnRoomSettling();
        }

        // ── Tick 驱动 ─────────────────────────────────────────────────

        /// <summary>
        /// 房间主循环 Tick，由 GlobalRoomManager 统一驱动。
        /// 房间实例从创建成功后即可进入持续 Tick，一直运行到房间销毁。
        /// Tick 持续存在不代表房间始终处于"正式对局进行中"，业务阶段由 RoomBusinessPhase 表达。
        /// Destroying 状态下禁止继续执行业务 Tick，只允许销毁链继续执行。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (LifecycleState == RoomLifecycleState.Destroying ||
                LifecycleState == RoomLifecycleState.Destroyed)
            {
                return;
            }

            CurrentTick++;

            // 分发 Tick 给所有已装配的业务组件
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnTick(deltaTime);
            }
        }

        // ── 销毁流程 ──────────────────────────────────────────────────

        /// <summary>
        /// 触发房间销毁流程。
        /// 一旦进入 Destroying 状态，后续重复销毁请求只输出 Warning 并立即返回。
        /// 进入 Destroying 后必须立即从 GlobalRoomManager 的可路由集合中移除（由 GlobalRoomManager 负责）。
        /// </summary>
        public void Destroy()
        {
            if (LifecycleState == RoomLifecycleState.Destroying)
            {
                Debug.LogWarning($"[RoomInstance] Destroy 重复调用：房间已处于 Destroying 状态，RoomId={RoomId}，已忽略。");
                return;
            }

            if (LifecycleState == RoomLifecycleState.Destroyed)
            {
                Debug.LogWarning($"[RoomInstance] Destroy 重复调用：房间已处于 Destroyed 状态，RoomId={RoomId}，已忽略。");
                return;
            }

            LifecycleState = RoomLifecycleState.Destroying;
            Debug.Log($"[RoomInstance] 房间开始销毁流程，RoomId={RoomId}，当前业务阶段={BusinessPhase}。");

            // 分发销毁回调给所有业务组件，按装配逆序执行
            DispatchOnRoomDestroy();

            // 清理房间域事件总线，确保销毁后不残留旧订阅
            EventBus.Clear();

            // 清理房间域消息路由器
            MessageRouter.ClearAll();

            // 清理房间作用域服务定位器
            RoomServiceLocator.Clear();

            // 录制器收尾（若已挂载）
            ReplayRecorder?.Finalize();

            // 清理成员集合
            _memberSessionIds.Clear();
            _onlineConnections.Clear();
            _components.Clear();

            LifecycleState = RoomLifecycleState.Destroyed;
            Debug.Log($"[RoomInstance] 房间销毁完成，RoomId={RoomId}。");
        }

        // ── 空置超时检测 ──────────────────────────────────────────────

        /// <summary>
        /// 由 GlobalRoomManager 在 Tick 中调用，检测房间是否已超过空置超时时长。
        /// 房间空置超时判断针对"当前在线连接映射是否为空"，不针对成员身份集合。
        /// </summary>
        public bool CheckEmptyTimeout(float emptyTimeoutSeconds)
        {
            if (!IsAllMembersOffline())
            {
                _emptyStartTime = -1f;
                return false;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_emptyStartTime < 0f)
            {
                _emptyStartTime = now;
                return false;
            }

            return (now - _emptyStartTime) >= emptyTimeoutSeconds;
        }

        // ── 生命周期回调分发（按装配顺序）───────────────────────────

        /// <summary>
        /// 分发房间创建回调，由 ServerRoomAssembler 在装配完成后调用。
        /// </summary>
        public void DispatchOnRoomCreate()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnRoomCreate();
            }
        }

        private void DispatchOnRoomWaitStart()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnRoomWaitStart();
            }
        }

        private void DispatchOnRoomStartGame()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnRoomStartGame();
            }
        }

        private void DispatchOnRoomGameEnding()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnRoomGameEnding();
            }
        }

        private void DispatchOnRoomSettling()
        {
            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnRoomSettling();
            }
        }

        /// <summary>
        /// 销毁回调按装配逆序执行，确保依赖关系正确清理。
        /// </summary>
        private void DispatchOnRoomDestroy()
        {
            for (int i = _components.Count - 1; i >= 0; i--)
            {
                _components[i].OnRoomDestroy();
            }
        }

        /// <summary>
        /// 获取当前所有成员的 SessionId 列表（成员身份集合快照）。
        /// 由 GlobalRoomManager 在房间销毁前调用，用于清空成员会话的 CurrentRoomId。
        /// 返回快照副本，不返回内部集合引用，防止外部修改。
        /// </summary>
        public List<string> GetMemberSessionIds()
        {
            return new List<string>(_memberSessionIds);
        }
    }

    /// <summary>
    /// 房间业务组件接口，定义所有房间业务组件必须实现的生命周期回调契约。
    /// 房间业务组件自身不定义独立的业务生命周期阶段，只响应 RoomInstance 的统一阶段回调。
    /// </summary>
    public interface IRoomComponent
    {
        void OnRoomCreate();
        void OnRoomWaitStart();
        void OnRoomStartGame();
        void OnRoomGameEnding();
        void OnRoomSettling();
        void OnTick(float deltaTime);
        void OnRoomDestroy();
    }
}