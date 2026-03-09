using System.Collections.Generic;
using StellarNet.Server.Network;
using StellarNet.Server.Room.Events;
using StellarNet.Server.Room.Services;
using StellarNet.Server.Room.Settings;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Server.Room.BuiltIn
{
    /// <summary>
    /// 房间基础设置业务组件 (Handle)。
    /// <para>职责：</para>
    /// <para>1. 维护房间基础信息（房主、人数上限、房间名）。</para>
    /// <para>2. 管理成员生命周期（加入、离开、断线重连）。</para>
    /// <para>3. 处理准备状态（Ready）逻辑。</para>
    /// <para>4. 核心修复：处理离开房间时的全局状态清理时序。</para>
    /// </summary>
    public sealed class ServerRoomBaseSettingsHandle :
        ServerRoomAssembler.IInitializableRoomComponent,
        IRoomBaseSettingsService
    {
        // 组件的唯一静态 ID，用于跨端映射和配置查找
        public const string StableComponentId = "room.base_settings";
        public string ComponentId => StableComponentId;

        // ─── 依赖项 ───
        private readonly ServerGlobalMessageSender _globalSender; // 用于发送全局消息（如离开房间后的回执）
        private readonly ServerRoomMessageSender _roomSender; // 用于发送房间内消息（如广播状态）
        private readonly SessionManager _sessionManager; // 用于 ConnectionId <-> SessionId 转换
        private readonly GlobalRoomManager _globalRoomManager; // [新增] 用于操作全局房间索引

        // ─── 运行时状态 ───
        private ServerRoomBaseSettingsModel _model; // 纯数据模型，负责逻辑计算
        private RoomInstance _room; // 持有当前房间的上下文引用
        private bool _isInitialized; // 防止未初始化调用的防御性标记

        /// <summary>
        /// 构造函数，支持依赖注入。
        /// </summary>
        public ServerRoomBaseSettingsHandle(
            ServerGlobalMessageSender globalSender,
            ServerRoomMessageSender roomSender,
            SessionManager sessionManager,
            GlobalRoomManager globalRoomManager) // [新增] 注入全局管理器
        {
            // 防御性编程：构造阶段拦截空依赖，防止运行时空指针
            if (globalSender == null) Debug.LogError("[ServerRoomBaseSettings] 构造失败：globalSender 缺失");
            if (roomSender == null) Debug.LogError("[ServerRoomBaseSettings] 构造失败：roomSender 缺失");
            if (sessionManager == null) Debug.LogError("[ServerRoomBaseSettings] 构造失败：sessionManager 缺失");
            if (globalRoomManager == null) Debug.LogError("[ServerRoomBaseSettings] 构造失败：globalRoomManager 缺失");

            _globalSender = globalSender;
            _roomSender = roomSender;
            _sessionManager = sessionManager;
            _globalRoomManager = globalRoomManager;
        }

        // ─── 生命周期与装配接口 ──────────────────────────────────────────

        /// <summary>
        /// 组件初始化入口。
        /// 在房间创建时被调用，用于读取配置并注册服务。
        /// </summary>
        public bool Init(RoomInstance roomInstance)
        {
            // 1. 基础校验
            if (roomInstance == null) return false;
            _room = roomInstance;

            // 2. 初始化数据模型
            _model = new ServerRoomBaseSettingsModel();

            // 3. 读取配置蓝图 (Blueprint)
            // 解释：房间创建时的参数（如最大人数、房间名）保存在 Settings 中，需同步到运行时 Model
            if (_room.Settings is GeneralRoomSettings settings)
            {
                _model.SetBaseInfo(settings.RoomName, string.Empty, settings.MaxMemberCount);
                Debug.Log($"[ServerRoomBaseSettings] 初始化完成: RoomId={_room.RoomId}, Max={settings.MaxMemberCount}");
            }
            else
            {
                Debug.LogWarning($"[ServerRoomBaseSettings] 警告：未找到通用房间配置，使用默认值。");
            }

            // 4. 注册服务接口，供其他组件调用 (Service Locator模式)
            _room.RoomServiceLocator.Register<IRoomBaseSettingsService>(this);

            _isInitialized = true;
            return true;
        }

        /// <summary>
        /// 组件销毁入口。
        /// 在房间销毁时调用，必须清理所有引用和状态。
        /// </summary>
        public void Deinit()
        {
            // 反注册服务
            if (_room != null && _room.RoomServiceLocator != null)
            {
                _room.RoomServiceLocator.Unregister<IRoomBaseSettingsService>();
            }

            ClearState();
            _room = null;
            _isInitialized = false;
        }

        /// <summary>
        /// 注册网络协议处理函数。
        /// 框架会自动根据 MessageType 路由消息到对应的 Handler。
        /// </summary>
        public IReadOnlyList<ServerRoomAssembler.RoomHandlerBinding> GetHandlerBindings()
        {
            return new List<ServerRoomAssembler.RoomHandlerBinding>
            {
                // 处理玩家准备/取消准备
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_SetReadyState),
                    Handler = OnC2S_SetReadyState
                },
                // 处理拉取成员列表请求
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_GetRoomMemberList),
                    Handler = OnC2S_GetRoomMemberList
                },
                // [关键] 处理主动离开房间请求
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_LeaveRoom),
                    Handler = OnC2S_LeaveRoom
                }
            };
        }

        // ─── 房间生命周期回调 (空实现保留用于后续扩展) ───
        public void OnRoomCreate()
        {
        }

        public void OnRoomWaitStart() => RecalculateCanStartAndBroadcastIfChanged();

        public void OnRoomStartGame()
        {
        }

        public void OnRoomGameEnding()
        {
        }

        public void OnRoomSettling()
        {
        }

        public void OnTick(float deltaTime)
        {
        }

        public void OnRoomDestroy() => ClearState();

        // ─── IRoomBaseSettingsService 接口实现 (业务逻辑核心) ───────────────────────

        /// <summary>
        /// 通知组件：有新成员加入了房间。
        /// 触发源：RoomInstance 底层逻辑。
        /// </summary>
        public void NotifyMemberJoined(string sessionId)
        {
            if (!EnsureAvailable("NotifyMemberJoined")) return;

            // 1. 更新 Model 数据
            _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);

            // 2. 房主自动分配逻辑 (如果是第一个人，或者房主为空)
            if (string.IsNullOrEmpty(_model.OwnerSessionId))
            {
                _model.SetOwner(sessionId);
            }
            // 防御性修正：确保 Model 里的房主和 RoomInstance 的房主一致
            else if (_model.OwnerSessionId == sessionId)
            {
                _model.SetOwner(sessionId);
            }

            // 3. 检查是否满足开始游戏条件
            RecalculateCanStartAndBroadcastIfChanged();

            // 4. 网络同步
            // 4.1 给新来的人发房间基础信息
            SendBaseSnapshotToMember(sessionId);
            // 4.2 给新来的人发成员列表
            SendMemberSnapshotToMember(sessionId);
            // 4.3 广播给其他人：有人来了
            var joinMsg = new S2C_MemberJoined { SessionId = sessionId };
            _roomSender.BroadcastToRoom(_room.RoomId, joinMsg);
            // 4.4 广播全量列表（可选，为了数据绝对一致性）
            BroadcastMemberSnapshotToRoom();

            // 5. 发布内部事件 (供服务端其他模块订阅)
            _room.EventBus.Publish(new RoomMemberJoinedEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId
            });
        }

        /// <summary>
        /// 通知组件：有成员离开了房间。
        /// 触发源：RoomInstance 底层逻辑 或 OnC2S_LeaveRoom。
        /// </summary>
        public void NotifyMemberLeft(string sessionId, string reason)
        {
            if (!EnsureAvailable("NotifyMemberLeft")) return;

            // 1. 从 Model 移除
            _model.RemoveMember(sessionId);

            // 2. 房主转移逻辑 (如果离开的是房主)
            if (_model.OwnerSessionId == sessionId)
            {
                string nextOwner = _model.SelectNextOwnerSessionId();
                _model.SetOwner(nextOwner); // 更新 Model

                // 如果选出了新房主，广播通知
                if (!string.IsNullOrEmpty(nextOwner))
                {
                    BroadcastOwnerChanged(nextOwner);
                }
            }

            // 3. 广播给剩余玩家：有人离开了
            BroadcastMemberLeft(sessionId, reason);

            // 4. 重新计算开始条件 (人数变少了可能就不满足了)
            RecalculateCanStartAndBroadcastIfChanged();

            // 5. 广播最新的成员列表快照
            BroadcastMemberSnapshotToRoom();

            // 6. 发布内部事件
            _room.EventBus.Publish(new RoomMemberLeftEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                Reason = reason ?? string.Empty
            });
        }

        /// <summary>
        /// 通知组件：成员断线重连恢复了。
        /// </summary>
        public void NotifyReconnectRecovered(string sessionId)
        {
            if (!EnsureAvailable("NotifyReconnectRecovered")) return;

            var member = _model.GetMember(sessionId);
            if (member != null)
            {
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: member.IsReady);
            }
            else
            {
                // 异常情况补救：如果 Model 里没这个人，重新加进去
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);
                if (_model.OwnerSessionId == sessionId) _model.SetOwner(sessionId);
            }

            // 重发所有状态给重连者
            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);
            BroadcastMemberSnapshotToRoom();
        }

        // ─── 数据获取接口 ───
        public List<RoomMemberSnapshot> GetMemberSnapshots() =>
            _model?.GetAllMembers() ?? new List<RoomMemberSnapshot>();

        public string GetOwnerSessionId() => _model?.OwnerSessionId ?? string.Empty;
        public bool GetCanStart() => _model?.CanStart ?? false;

        /// <summary>
        /// 构建房间基础信息快照 (用于发送给客户端)。
        /// </summary>
        public S2C_RoomBaseSettingsSnapshot BuildBaseSnapshot()
        {
            if (!EnsureAvailable("BuildBaseSnapshot")) return null;
            return new S2C_RoomBaseSettingsSnapshot
            {
                RoomId = _room.RoomId,
                RoomName = _model.RoomName,
                MaxMemberCount = _model.MaxMemberCount,
                OwnerSessionId = _model.OwnerSessionId,
                CanStart = _model.CanStart
            };
        }

        public void ClearState() => _model?.Clear();

        // ─── 协议处理逻辑 (Network Handlers) ──────────────────────────────────────────────────

        /// <summary>
        /// 处理客户端请求：准备/取消准备。
        /// </summary>
        private void OnC2S_SetReadyState(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_SetReadyState")) return;

            // 1. 解析消息
            var message = rawMessage as C2S_SetReadyState;
            if (message == null) return;

            // 2. 身份校验
            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            var member = _model.GetMember(sessionId);
            if (member == null) return;

            // 3. 状态去重 (如果状态没变就不处理)
            if (member.IsReady == message.IsReady) return;

            // 4. 更新状态
            _model.AddOrUpdateMember(sessionId, member.IsOnline, message.IsReady);

            // 5. 广播变更
            var changed = new S2C_MemberReadyStateChanged
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                IsReady = message.IsReady
            };
            _roomSender.BroadcastToRoom(_room.RoomId, changed);

            // 6. 触发事件
            _room.EventBus.Publish(new RoomReadyStateChangedEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                IsReady = message.IsReady
            });

            // 7. 检查是否全员准备好了
            RecalculateCanStartAndBroadcastIfChanged();
        }

        /// <summary>
        /// 处理客户端请求：获取成员列表。
        /// </summary>
        private void OnC2S_GetRoomMemberList(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_GetRoomMemberList")) return;
            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            SendMemberSnapshotToMember(sessionId);
        }

        /// <summary>
        /// [核心修复] 处理客户端请求：离开房间。
        /// </summary>
        private void OnC2S_LeaveRoom(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_LeaveRoom")) return;

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[ServerRoomBaseSettings] 离开失败：无法解析 SessionId，ConnId={connectionId}");
                return;
            }

            Debug.Log($"[ServerRoomBaseSettings] 收到离开请求：SessionId={sessionId}, RoomId={roomId}");

            // ──────────────────────────────────────────────────────────────────────────
            // 核心时序修复：必须先清理服务端数据，再通知客户端。
            // ──────────────────────────────────────────────────────────────────────────

            // 步骤 1: 执行核心数据清理
            // 调用 GlobalRoomManager 强制移除成员。
            // 这会更新全局 SessionId -> RoomId 映射表，确保该玩家被标记为“无房间状态”。
            // 注意：RemoveMember 内部通常会回调 NotifyMemberLeft，处理广播等逻辑。
            _globalRoomManager.RemoveMember(sessionId);

            // 步骤 2: 发送成功回执
            // 此时服务端状态已清理完毕。即使客户端收到消息后 1ms 内发起 CreateRoom，
            // GlobalRoomManager 查表时也会发现该玩家不在任何房间，从而允许建房。
            // 注意：使用 _globalSender 发送，因为玩家逻辑上已经不在这个房间了。
            _globalSender.SendToSession(sessionId, new S2C_LeaveRoomResult { Success = true });
        }

        // ─── 内部辅助方法 (Private Helpers) ──────────────────────────────────────────────────

        private void SendBaseSnapshotToMember(string sessionId)
        {
            var snapshot = BuildBaseSnapshot();
            if (snapshot != null) _roomSender.SendToRoomMember(_room.RoomId, sessionId, snapshot);
        }

        private void SendMemberSnapshotToMember(string sessionId)
        {
            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = _model.GetAllMembers().ToArray()
            };
            _roomSender.SendToRoomMember(_room.RoomId, sessionId, snapshot);
        }

        private void BroadcastMemberSnapshotToRoom()
        {
            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = _model.GetAllMembers().ToArray()
            };
            _roomSender.BroadcastToRoom(_room.RoomId, snapshot);
        }

        private void BroadcastMemberLeft(string sessionId, string reason)
        {
            var message = new S2C_MemberLeft { SessionId = sessionId, Reason = reason ?? string.Empty };
            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void BroadcastOwnerChanged(string newOwnerSessionId)
        {
            var message = new S2C_RoomOwnerChanged { RoomId = _room.RoomId, NewOwnerSessionId = newOwnerSessionId };
            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void RecalculateCanStartAndBroadcastIfChanged()
        {
            bool oldCanStart = _model.CanStart;
            bool newCanStart = _model.CalculateCanStart();
            _model.SetCanStart(newCanStart);

            if (oldCanStart == newCanStart) return;

            var changed = new S2C_RoomCanStartStateChanged
            {
                RoomId = _room.RoomId,
                CanStart = newCanStart
            };
            _roomSender.BroadcastToRoom(_room.RoomId, changed);

            _room.EventBus.Publish(new RoomCanStartStateChangedEvent
            {
                RoomId = _room.RoomId,
                CanStart = newCanStart
            });
        }

        private string ResolveSessionIdByConnection(ConnectionId connectionId)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            return session?.SessionId ?? string.Empty;
        }

        // 防御性检查：确保组件已初始化且环境正常
        private bool EnsureAvailable(string caller)
        {
            if (!_isInitialized || _room == null)
            {
                Debug.LogError($"[ServerRoomBaseSettings] {caller} 失败：组件未初始化或 Room 为空。");
                return false;
            }

            return true;
        }
    }
}