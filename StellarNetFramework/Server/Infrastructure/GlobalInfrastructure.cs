// Assets/StellarNetFramework/Server/Infrastructure/GlobalInfrastructure.cs

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Server.Config;
using StellarNet.Server.Infrastructure.EventBus;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Adapter;
using StellarNet.Server.Network.Entry;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;
using StellarNet.Server.Room;
using StellarNet.Server.Modules;
using StellarNet.Server.Modules.Replay;

namespace StellarNet.Server.Infrastructure
{
    // 服务端顶级门面，是整个框架服务端的唯一装配入口与生命周期驱动者。
    // 职责：
    //   1. 按依赖顺序完成所有基础设施模块的构建与装配
    //   2. 向 ServerNetworkEntry 注入房间域路由委托
    //   3. 向 ServerSendCoordinator 注入 ReplayRecorder 查询委托与全体广播委托
    //   4. 向 GlobalRoomManager 注入空置销毁回调
    //   5. 统一驱动每帧 Tick
    //   6. 统一执行关停流程
    // 业务层只需继承 GlobalInfrastructure 并实现 OnConfigure() 完成业务委托注入，
    // 不得绕过本类直接操作任何基础设施模块。
    public abstract class GlobalInfrastructure : MonoBehaviour
    {
        // ── 基础设施模块 ──────────────────────────────────────────────────────
        protected NetConfigManager ConfigManager { get; private set; }
        protected GlobalServiceLocator ServiceLocator { get; private set; }
        protected GlobalEventBus EventBus { get; private set; }
        protected MessageRegistry MessageRegistry { get; private set; }
        protected INetworkAdapter NetworkAdapter { get; private set; }
        protected SessionManager SessionManager { get; private set; }
        protected GlobalRoomManager RoomManager { get; private set; }
        protected ServerGlobalMessageRouter GlobalRouter { get; private set; }
        protected ServerRoomMessageSender RoomSender { get; private set; }
        protected ServerGlobalMessageSender GlobalSender { get; private set; }
        protected ServerSendCoordinator SendCoordinator { get; private set; }
        protected ServerNetworkEntry NetworkEntry { get; private set; }
        protected UserModule UserModule { get; private set; }
        protected RoomDispatcherModule RoomDispatcher { get; private set; }
        protected ReconnectModule ReconnectModule { get; private set; }
        protected ReplayModule ReplayModule { get; private set; }
        protected FriendModule FriendModule { get; private set; }
        protected GlobalChatModule ChatModule { get; private set; }
        protected AnnouncementModule AnnouncementModule { get; private set; }
        protected IdempotentCache IdempotentCache { get; private set; }

        // 当前帧时间戳（Unix 毫秒），每帧 Tick 开始时更新，统一作为时间源
        protected long NowUnixMs { get; private set; }

        private bool _isInitialized = false;
        private bool _isShutdown = false;

        private const long SessionExpireCheckIntervalMs = 5000;
        private long _lastSessionExpireCheckMs = 0;

        private const long IdempotentExpireCheckIntervalMs = 10000;
        private long _lastIdempotentExpireCheckMs = 0;

        // ── Unity 生命周期 ────────────────────────────────────────────────────

        private void Awake()
        {
            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            BuildInfrastructure();
        }

        private void Start()
        {
            if (!_isInitialized)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] Start 阶段检测到初始化未完成，" +
                    $"物体={name}，服务端将不会启动。");
                return;
            }

            NetworkAdapter.Start();
        }

        private void Update()
        {
            if (!_isInitialized || _isShutdown)
                return;

            NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var deltaMs = (long)(Time.deltaTime * 1000f);

            EventBus.ResetFrameCounter();
            RoomManager.Tick(deltaMs, NowUnixMs);

            if (NowUnixMs - _lastSessionExpireCheckMs >= SessionExpireCheckIntervalMs)
            {
                SessionManager.TickSessionExpireCheck(NowUnixMs);
                _lastSessionExpireCheckMs = NowUnixMs;
            }

            if (NowUnixMs - _lastIdempotentExpireCheckMs >= IdempotentExpireCheckIntervalMs)
            {
                IdempotentCache.TickExpireCheck(NowUnixMs);
                _lastIdempotentExpireCheckMs = NowUnixMs;
            }

            OnTick(deltaMs, NowUnixMs);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        // ── 装配流程 ──────────────────────────────────────────────────────────

        private void BuildInfrastructure()
        {
            // 步骤一：配置管理器
            ConfigManager = new NetConfigManager();
            var initialJson = LoadInitialConfigJson();
            if (!string.IsNullOrEmpty(initialJson))
                ConfigManager.LoadFromJson(initialJson);
            var config = ConfigManager.Current;

            // 步骤二：全局服务定位器与 EventBus
            ServiceLocator = new GlobalServiceLocator();
            EventBus = new GlobalEventBus(config.EventBusWarningThreshold);

            // 步骤三：MessageRegistry
            var assemblies = GetMessageRegistryAssemblies();
            if (assemblies == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] 装配失败：GetMessageRegistryAssemblies() 返回 null，" +
                    $"物体={name}");
                return;
            }

            MessageRegistry = MessageRegistryBuilder.Build(assemblies);

            // 步骤四：序列化器
            var serializer = CreateSerializer();
            if (serializer == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] 装配失败：CreateSerializer() 返回 null，物体={name}");
                return;
            }

            // 步骤五：Adapter
            NetworkAdapter = CreateNetworkAdapter();
            if (NetworkAdapter == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] 装配失败：CreateNetworkAdapter() 返回 null，物体={name}");
                return;
            }

            // 步骤六：SessionManager
            SessionManager = new SessionManager(config.SessionRetainTimeoutMs);

            // 步骤七：发送链
            SendCoordinator = new ServerSendCoordinator(NetworkAdapter);
            GlobalRouter = new ServerGlobalMessageRouter();
            RoomSender = new ServerRoomMessageSender(MessageRegistry, serializer, SendCoordinator);
            GlobalSender = new ServerGlobalMessageSender(
                MessageRegistry, serializer, SessionManager, SendCoordinator);

            // 步骤八：注入全体广播委托，通过 SessionManager 公开方法枚举在线会话
            SendCoordinator.SetBroadcastToAllDelegate((envelope, deliveryMode) =>
            {
                foreach (var session in SessionManager.GetAllOnlineSessions())
                {
                    NetworkAdapter.Send(session.ConnectionId, envelope, deliveryMode);
                }
            });

            // 步骤九：GlobalRoomManager，传入 RoomSender 供装配器注入组件
            RoomManager = new GlobalRoomManager(RoomSender, config.EmptyRoomTimeoutMs);

            // 步骤十：注入 ReplayRecorder 查询委托
            SendCoordinator.SetReplayRecorderResolver(RoomManager.ResolveReplayRecorder);

            // 步骤十一：注入空置房间销毁回调，通过 SessionManager 公开方法枚举房间内会话
            RoomManager.SetOnEmptyRoomDestroyCallback(roomId =>
            {
                foreach (var session in SessionManager.GetAllSessionsInRoom(roomId))
                {
                    SessionManager.ClearSessionRoom(session.SessionId);
                }
            });

            // 步骤十二：NetworkEntry
            NetworkEntry = new ServerNetworkEntry(
                MessageRegistry, serializer, SessionManager, GlobalRouter);
            NetworkEntry.SetRoomDomainRouter(RoomManager.RouteRoomMessage);

            NetworkAdapter.OnDataReceived += NetworkEntry.OnAdapterDataReceived;
            NetworkAdapter.OnConnected += OnAdapterConnected;
            NetworkAdapter.OnDisconnected += OnAdapterDisconnected;

            // 步骤十三：全局功能模块
            UserModule = new UserModule(SessionManager, GlobalRouter, GlobalSender);
            UserModule.SetNowUnixMsProvider(() => NowUnixMs);

            RoomDispatcher = new RoomDispatcherModule(
                SessionManager, RoomManager, GlobalRouter, GlobalSender);

            ReconnectModule = new ReconnectModule(
                SessionManager, RoomManager, GlobalRouter, GlobalSender, NetworkAdapter);
            ReconnectModule.SetNowUnixMsProvider(() => NowUnixMs);

            ReplayModule = new ReplayModule(RoomManager);

            FriendModule = new FriendModule(SessionManager, GlobalRouter, GlobalSender);
            ChatModule = new GlobalChatModule(SessionManager, GlobalRouter, GlobalSender);
            AnnouncementModule = new AnnouncementModule(GlobalSender);

            // 步骤十四：幂等缓存
            IdempotentCache = new IdempotentCache(
                config.IdempotentCacheMaxSize,
                config.IdempotentCacheExpireMs);

            // 步骤十五：配置热重载回调
            ConfigManager.RegisterReloadCallback(OnConfigReloaded);

            // 步骤十六：注册核心模块到 GlobalServiceLocator
            ServiceLocator.Register<GlobalEventBus>(EventBus);
            ServiceLocator.Register<SessionManager>(SessionManager);
            ServiceLocator.Register<GlobalRoomManager>(RoomManager);
            ServiceLocator.Register<UserModule>(UserModule);
            ServiceLocator.Register<RoomDispatcherModule>(RoomDispatcher);
            ServiceLocator.Register<ReconnectModule>(ReconnectModule);
            ServiceLocator.Register<ReplayModule>(ReplayModule);
            ServiceLocator.Register<FriendModule>(FriendModule);
            ServiceLocator.Register<GlobalChatModule>(ChatModule);
            ServiceLocator.Register<AnnouncementModule>(AnnouncementModule);
            ServiceLocator.Register<IdempotentCache>(IdempotentCache);
            ServiceLocator.Register<NetConfigManager>(ConfigManager);

            // 步骤十七：业务层配置扩展点
            OnConfigure();

            _isInitialized = true;
        }

        // ── 连接事件处理 ──────────────────────────────────────────────────────

        private void OnAdapterConnected(Shared.Identity.ConnectionId connectionId)
        {
            OnConnectionEstablished(connectionId);
        }

        private void OnAdapterDisconnected(
            Shared.Identity.ConnectionId connectionId,
            string reason)
        {
            SessionManager.OnConnectionDisconnected(connectionId, NowUnixMs);

            // 通过 SessionManager 公开方法查询会话，不穿透内部字段
            var session = SessionManager.GetSessionByConnection(connectionId);
            if (session != null && session.IsInRoom)
            {
                var room = RoomManager.GetRoutableRoom(session.CurrentRoomId);
                room?.SetMemberOffline(connectionId);
            }

            OnConnectionLost(connectionId, reason);
        }

        // ── 配置热重载 ────────────────────────────────────────────────────────

        private void OnConfigReloaded(NetConfig config)
        {
            SessionManager.UpdateSessionRetainTimeout(config.SessionRetainTimeoutMs);
            RoomManager.UpdateEmptyRoomTimeout(config.EmptyRoomTimeoutMs);
            IdempotentCache.UpdateConfig(
                config.IdempotentCacheMaxSize,
                config.IdempotentCacheExpireMs);
        }

        // ── 关停流程 ──────────────────────────────────────────────────────────

        private void Shutdown()
        {
            if (_isShutdown)
                return;

            _isShutdown = true;

            if (NetworkAdapter != null)
            {
                NetworkAdapter.OnConnected -= OnAdapterConnected;
                NetworkAdapter.OnDisconnected -= OnAdapterDisconnected;
                NetworkAdapter.OnDataReceived -= NetworkEntry.OnAdapterDataReceived;

                if (NetworkAdapter is MirrorNetworkAdapter mirrorAdapter)
                    mirrorAdapter.Stop();
            }

            RoomManager?.ShutdownAll();
            SessionManager?.Clear();
            IdempotentCache?.Clear();
            EventBus?.Clear();
            ServiceLocator?.Clear();

            OnShutdown();
        }

        // ── 抽象与虚方法 ──────────────────────────────────────────────────────

        protected abstract IEnumerable<Assembly> GetMessageRegistryAssemblies();
        protected abstract ISerializer CreateSerializer();
        protected abstract INetworkAdapter CreateNetworkAdapter();
        protected virtual string LoadInitialConfigJson() => null;
        protected abstract void OnConfigure();

        protected virtual void OnTick(long deltaTimeMs, long nowUnixMs)
        {
        }

        protected virtual void OnConnectionEstablished(Shared.Identity.ConnectionId connectionId)
        {
        }

        protected virtual void OnConnectionLost(
            Shared.Identity.ConnectionId connectionId,
            string reason)
        {
        }

        protected virtual void OnShutdown()
        {
        }
    }
}