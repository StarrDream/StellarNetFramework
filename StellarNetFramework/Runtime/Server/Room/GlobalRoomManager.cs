using System.Collections.Generic;
using System.Linq;
using StellarNet.Server.Replay;
using StellarNet.Server.Room.Services;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.RoomSettings;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间物理宿主与调度核心，作为服务端核心全局服务注册到 GlobalScope ServiceLocator。
    /// 职责严格限定为：持有 RoomInstance 集合、驱动所有房间 Tick、维护可路由房间集合、
    /// 执行房间空置巡检与生命周期收尾、作为全局模块访问房间运行时能力的统一代理门面。
    /// 全局模块不得直接穿透目标房间的 RoomScope ServiceLocator 获取内部服务，
    /// 必须通过此类提供的公开代理接口完成跨域访问。
    /// </summary>
    public sealed class GlobalRoomManager : IGlobalService
    {
        private readonly Dictionary<string, RoomInstance> _routableRooms
            = new Dictionary<string, RoomInstance>();

        private readonly List<RoomInstance> _allRooms = new List<RoomInstance>();
        private readonly List<RoomInstance> _pendingRemoval = new List<RoomInstance>();

        private readonly SessionManager _sessionManager;
        private readonly ServerRoomAssembler _assembler;
        private readonly RoomComponentRegistry _componentRegistry;

        private float _roomEmptyTimeoutSeconds;

        public GlobalRoomManager(
            SessionManager sessionManager,
            ServerRoomAssembler assembler,
            RoomComponentRegistry componentRegistry,
            float roomEmptyTimeoutSeconds)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[GlobalRoomManager] 构造失败：sessionManager 为 null。");
                return;
            }

            if (assembler == null)
            {
                Debug.LogError("[GlobalRoomManager] 构造失败：assembler 为 null。");
                return;
            }

            if (componentRegistry == null)
            {
                Debug.LogError("[GlobalRoomManager] 构造失败：componentRegistry 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _assembler = assembler;
            _componentRegistry = componentRegistry;
            _roomEmptyTimeoutSeconds = roomEmptyTimeoutSeconds;
        }

        public void UpdateEmptyTimeout(float timeoutSeconds)
        {
            _roomEmptyTimeoutSeconds = timeoutSeconds;
        }

        public RoomInstance CreateRoom(
            string roomId,
            IRoomSettings settings,
            string[] componentIds,
            bool enableReplay = false)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[GlobalRoomManager] CreateRoom 失败：roomId 为空。");
                return null;
            }

            if (settings == null)
            {
                Debug.LogError($"[GlobalRoomManager] CreateRoom 失败：settings 为 null，RoomId={roomId}。");
                return null;
            }

            if (_routableRooms.ContainsKey(roomId))
            {
                Debug.LogError($"[GlobalRoomManager] CreateRoom 失败：RoomId={roomId} 已存在于可路由集合中，禁止重复创建。");
                return null;
            }

            var room = new RoomInstance(roomId, settings);

            if (enableReplay)
            {
                var recorder = new ReplayRecorder(roomId, settings);
                room.MountReplayRecorder(recorder);
            }

            bool assembleSuccess = _assembler.Assemble(room, componentIds, _componentRegistry);
            if (!assembleSuccess)
            {
                Debug.LogError($"[GlobalRoomManager] CreateRoom 失败：房间 {roomId} 组件装配失败，已执行原子回滚，房间不会进入可路由集合。");
                return null;
            }

            _routableRooms[roomId] = room;
            _allRooms.Add(room);

            room.AdvanceToWaitingForStart();

            Debug.Log(
                $"[GlobalRoomManager] 房间创建成功，RoomId={roomId}，组件数量={componentIds?.Length ?? 0}，录制启用={enableReplay}。");
            return room;
        }

        public void RouteRoomMessage(
            string roomId,
            ConnectionId connectionId,
            StellarNet.Shared.Registry.MessageMetadata metadata,
            object message)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] RouteRoomMessage 失败：roomId 为空，ConnectionId={connectionId}，MessageId={metadata?.MessageId}。");
                return;
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] RouteRoomMessage 失败：RoomId={roomId} 不在可路由集合中，ConnectionId={connectionId}，MessageId={metadata?.MessageId}，消息已丢弃。");
                return;
            }

            room.MessageRouter.Dispatch(connectionId, roomId, metadata, message);
        }

        public List<ConnectionId> GetOnlineConnectionIds(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return new List<ConnectionId>();
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                return new List<ConnectionId>();
            }

            return room.GetOnlineConnectionIds();
        }

        public ReplayRecorder GetReplayRecorder(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            return room.ReplayRecorder;
        }

        public RoomBusinessPhase GetRoomBusinessPhase(string roomId)
        {
            if (string.IsNullOrEmpty(roomId) || !_routableRooms.TryGetValue(roomId, out var room))
            {
                return RoomBusinessPhase.Ended;
            }

            return room.BusinessPhase;
        }

        public RoomInstance GetRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            _routableRooms.TryGetValue(roomId, out var room);
            return room;
        }

        /// <summary>
        /// 将成员从房间中移除。
        /// <para>职责：</para>
        /// <para>1. 查找成员当前所在的房间。</para>
        /// <para>2. 通知房间内部业务组件（如 BaseSettings）执行离房逻辑（广播、换房主）。</para>
        /// <para>3. 解除 Session 与 Room 的绑定关系，使其恢复自由身。</para>
        /// <para>4. 检查房间是否变空，触发销毁倒计时。</para>
        /// </summary>
        /// <param name="sessionId">要移除的成员 SessionId</param>
        /// <param name="reason">离开原因（默认"主动离开"）</param>
        public void RemoveMember(string sessionId, string reason = "主动离开")
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("[GlobalRoomManager] RemoveMember 失败：sessionId 为空。");
                return;
            }

            // 1. 获取 Session 对象
            var session = _sessionManager.GetSessionById(sessionId);
            if (session == null)
            {
                Debug.LogWarning($"[GlobalRoomManager] RemoveMember 警告：SessionId={sessionId} 不在线或不存在。");
                return;
            }

            // 2. 检查该 Session 当前是否在房间中
            string currentRoomId = session.CurrentRoomId;
            if (string.IsNullOrEmpty(currentRoomId))
            {
                Debug.LogWarning($"[GlobalRoomManager] RemoveMember 警告：SessionId={sessionId} 当前不在任何房间中。");
                return;
            }

            // 3. 获取房间实例
            if (!_routableRooms.TryGetValue(currentRoomId, out var room))
            {
                // 异常情况：Session 记录了 RoomId，但 GlobalManager 里找不到这个房间
                // 强制修正 Session 状态
                Debug.LogError($"[GlobalRoomManager] RemoveMember 异常：房间 {currentRoomId} 不存在，强制重置 Session 状态。");
                session.UnbindRoom();
                return;
            }

            // 4. 通知房间内部业务组件 (触发广播、房主转移等)
            // 注意：这里调用的是 TryNotifyRoomMemberLeft，它会通过 ServiceLocator 找到 BaseSettings 组件
            bool notifySuccess = TryNotifyRoomMemberLeft(currentRoomId, sessionId, reason);
            if (!notifySuccess)
            {
                Debug.LogWarning($"[GlobalRoomManager] RemoveMember 警告：未能通知房间 {currentRoomId} 业务组件成员离开。");
            }

            // 5. [关键] 解除绑定 (更新全局索引)
            // 这步执行完后，session.CurrentRoomId 变为空，允许再次建房
            session.UnbindRoom();

            Debug.Log($"[GlobalRoomManager] 成员移除成功：SessionId={sessionId}, RoomId={currentRoomId}, Reason={reason}");

            // 6. 检查房间是否空置 (由 Tick 中的 CheckEmptyTimeout 处理，这里无需立即销毁)
        }

        /// <summary>
        /// 通知目标房间的基础设置组件有成员加入。
        /// 这是全局模块访问房间骨架组件的统一跨域代理入口。
        /// </summary>
        public bool TryNotifyRoomMemberJoined(string roomId, string sessionId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError($"[GlobalRoomManager] TryNotifyRoomMemberJoined 失败：roomId 为空，SessionId={sessionId}。");
                return false;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[GlobalRoomManager] TryNotifyRoomMemberJoined 失败：sessionId 为空，RoomId={roomId}。");
                return false;
            }

            var service = GetRoomBaseSettingsService(roomId);
            if (service == null)
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomMemberJoined 失败：RoomId={roomId} 缺失 IRoomBaseSettingsService，SessionId={sessionId}。");
                return false;
            }

            service.NotifyMemberJoined(sessionId);
            return true;
        }

        /// <summary>
        /// 通知目标房间的基础设置组件有成员离开。
        /// 这是全局模块访问房间骨架组件的统一跨域代理入口。
        /// </summary>
        public bool TryNotifyRoomMemberLeft(string roomId, string sessionId, string reason)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomMemberLeft 失败：roomId 为空，SessionId={sessionId}，Reason={reason}。");
                return false;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomMemberLeft 失败：sessionId 为空，RoomId={roomId}，Reason={reason}。");
                return false;
            }

            var service = GetRoomBaseSettingsService(roomId);
            if (service == null)
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomMemberLeft 失败：RoomId={roomId} 缺失 IRoomBaseSettingsService，SessionId={sessionId}，Reason={reason}。");
                return false;
            }

            service.NotifyMemberLeft(sessionId, reason);
            return true;
        }

        /// <summary>
        /// 通知目标房间的基础设置组件某成员完成重连接管。
        /// 由全局重连模块通过此代理入口触发房间骨架快照补发。
        /// </summary>
        public bool TryNotifyRoomReconnectRecovered(string roomId, string sessionId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomReconnectRecovered 失败：roomId 为空，SessionId={sessionId}。");
                return false;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError($"[GlobalRoomManager] TryNotifyRoomReconnectRecovered 失败：sessionId 为空，RoomId={roomId}。");
                return false;
            }

            var service = GetRoomBaseSettingsService(roomId);
            if (service == null)
            {
                Debug.LogError(
                    $"[GlobalRoomManager] TryNotifyRoomReconnectRecovered 失败：RoomId={roomId} 缺失 IRoomBaseSettingsService，SessionId={sessionId}。");
                return false;
            }

            service.NotifyReconnectRecovered(sessionId);
            return true;
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _allRooms.Count; i++)
            {
                var room = _allRooms[i];
                if (room.LifecycleState == RoomLifecycleState.Running)
                {
                    room.Tick(deltaTime);

                    if (room.CheckEmptyTimeout(_roomEmptyTimeoutSeconds))
                    {
                        Debug.Log($"[GlobalRoomManager] 房间空置超时，强制销毁，RoomId={room.RoomId}。");
                        DestroyRoom(room.RoomId, "空置超时");
                    }
                }
            }

            CollectDestroyedRooms();
        }

        public void DestroyRoom(string roomId, string reason = "")
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[GlobalRoomManager] DestroyRoom 失败：roomId 为空。");
                return;
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                Debug.LogWarning($"[GlobalRoomManager] DestroyRoom 警告：RoomId={roomId} 不在可路由集合中，可能已被销毁，原因={reason}。");
                return;
            }

            _routableRooms.Remove(roomId);
            Debug.Log($"[GlobalRoomManager] 房间从可路由集合移除，RoomId={roomId}，原因={reason}。");

            NotifySessionsRoomDestroyed(room);
            room.Destroy();
        }

        public void DestroyAllRooms()
        {
            var roomIds = new List<string>(_routableRooms.Keys);
            foreach (var roomId in roomIds)
            {
                DestroyRoom(roomId, "服务器关闭");
            }
        }

        private IRoomBaseSettingsService GetRoomBaseSettingsService(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                return null;
            }

            if (!_routableRooms.TryGetValue(roomId, out var room))
            {
                return null;
            }

            if (room.RoomServiceLocator == null)
            {
                Debug.LogError(
                    $"[GlobalRoomManager] GetRoomBaseSettingsService 失败：RoomId={roomId} 的 RoomServiceLocator 为 null。");
                return null;
            }

            return room.RoomServiceLocator.Get<IRoomBaseSettingsService>();
        }

        private void NotifySessionsRoomDestroyed(RoomInstance room)
        {
            var memberSessionIds = room.GetMemberSessionIds();
            foreach (var sessionId in memberSessionIds)
            {
                var session = _sessionManager.GetSessionById(sessionId);
                if (session != null)
                {
                    session.UnbindRoom();
                    Debug.Log($"[GlobalRoomManager] 会话房间绑定已清除，SessionId={sessionId}，RoomId={room.RoomId}。");
                }
            }
        }

        /// <summary>
        /// 获取分页的活跃房间列表。
        /// </summary>
        public List<RoomInstance> GetPagedRoomList(int pageIndex, int pageSize, out int totalCount)
        {
            // 1. 筛选活跃房间 (Running 状态)
            // 这里可以根据需求扩展筛选条件，比如只返回 WaitingForStart 阶段的房间
            var activeRooms = _allRooms
                .Where(r => r.LifecycleState == RoomLifecycleState.Running)
                .ToList();

            totalCount = activeRooms.Count;

            // 2. 执行分页
            if (pageIndex < 0) pageIndex = 0;
            if (pageSize <= 0) pageSize = 10;

            return activeRooms
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToList();
        }

        private void CollectDestroyedRooms()
        {
            _pendingRemoval.Clear();
            for (int i = 0; i < _allRooms.Count; i++)
            {
                if (_allRooms[i].LifecycleState == RoomLifecycleState.Destroyed)
                {
                    _pendingRemoval.Add(_allRooms[i]);
                }
            }

            foreach (var room in _pendingRemoval)
            {
                _allRooms.Remove(room);
            }
        }
    }
}