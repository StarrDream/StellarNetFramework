using System.Collections.Generic;
using System.Reflection;
using StellarNet.Server.Adapter;
using StellarNet.Server.Config;
using StellarNet.Server.EventBus;
using StellarNet.Server.GlobalModules.Announcement;
using StellarNet.Server.GlobalModules.LobbyChat;
using StellarNet.Server.GlobalModules.ReplayModule;
using StellarNet.Server.GlobalModules.RoomDispatcher;
using StellarNet.Server.GlobalModules.User;
using StellarNet.Server.Idempotent;
using StellarNet.Server.Network;
using StellarNet.Server.Room;
using StellarNet.Server.Sender;
using StellarNet.Server.ServiceLocator;
using StellarNet.Server.Session;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Server
{
    /// <summary>
    /// 服务端全局装配入口，是整个服务端框架的唯一启动点与生命周期宿主。
    /// 负责按正确依赖顺序创建所有服务端基础设施对象、完成依赖注入、绑定事件与启动监听。
    /// 持有所有顶层对象的强引用，防止 GC 提前回收。
    /// Tick 驱动由 Unity MonoBehaviour Update 调用，不使用独立线程。
    /// Shutdown 流程由 OnApplicationQuit 触发，确保所有房间与录制器正常收尾。
    /// 此类不承担任何业务逻辑，只负责装配与生命周期管理。
    /// </summary>
    public sealed class GlobalInfrastructure : MonoBehaviour
    {
        [Header("配置文件路径")] [SerializeField] private string _netConfigFilePath = "Config/NetConfig.json";

        [Header("Mirror 适配器引用")] [SerializeField]
        private MirrorServerAdapter _mirrorAdapter;

        // 顶层对象强引用（防止 GC 提前回收）
        private NetConfigManager _configManager;
        private NewtonsoftJsonSerializer _serializer;
        private MessageRegistry _messageRegistry;
        private SessionManager _sessionManager;
        private GlobalEventBus _globalEventBus;
        private GlobalScopeServiceLocator _globalServiceLocator;
        private ServerGlobalMessageRouter _globalRouter;
        private GlobalMessageRegistrar _globalRegistrar;
        private RoomOwnershipValidator _ownershipValidator;
        private ServerNetworkEntry _networkEntry;
        private RoomComponentRegistry _componentRegistry;
        private ServerRoomAssembler _roomAssembler;
        private GlobalRoomManager _roomManager;
        private ServerSendCoordinator _sendCoordinator;
        private ServerGlobalMessageSender _globalSender;
        private ServerRoomMessageSender _roomSender;
        private IdempotentCache _idempotentCache;

        // 全局模块
        private UserModel _userModel;
        private UserHandle _userHandle;
        private StellarNet.Server.GlobalModules.Reconnect.ReconnectModel _reconnectModel;
        private StellarNet.Server.GlobalModules.Reconnect.ReconnectHandle _reconnectHandle;
        private RoomDispatcherModel _roomDispatcherModel;
        private RoomDispatcherHandle _roomDispatcherHandle;
        private ReplayModel _replayModel;
        private ReplayHandle _replayHandle;
        private AnnouncementModel _announcementModel;
        private AnnouncementHandle _announcementHandle;
        private LobbyChatModel _lobbyChatModel;
        private LobbyChatHandle _lobbyChatHandle;

        private bool _isInitialized;

        private void Awake()
        {
            if (_mirrorAdapter == null)
            {
                Debug.LogError($"[GlobalInfrastructure] Awake 失败：物体 {name} 未挂载 MirrorServerAdapter，服务端无法启动。");
                return;
            }

            Initialize();
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _sessionManager?.Tick();
            _idempotentCache?.Tick();
            _roomManager?.Tick(deltaTime);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        /// <summary>
        /// 按正确依赖顺序完成所有服务端基础设施对象的创建、注入与绑定。
        /// 任一关键依赖创建失败时输出 Error 并阻止后续装配，防止产生半初始化状态。
        /// </summary>
        private void Initialize()
        {
            _configManager = new NetConfigManager(_netConfigFilePath);
            var config = _configManager.Current;
            if (config == null)
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：物体 {name} 的 NetConfig 为空。");
                return;
            }

            _serializer = new NewtonsoftJsonSerializer();
            if (_serializer == null)
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：物体 {name} 的序列化器创建失败。");
                return;
            }

            _messageRegistry = BuildMessageRegistry();
            if (_messageRegistry == null)
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：物体 {name} 的 MessageRegistry 构建失败。");
                return;
            }

            _sessionManager = new SessionManager(config.SessionRetainTimeoutSeconds);
            _globalEventBus = new GlobalEventBus();
            _globalServiceLocator = new GlobalScopeServiceLocator();
            _globalRouter = new ServerGlobalMessageRouter();
            _globalRegistrar = new GlobalMessageRegistrar(_globalRouter);
            _ownershipValidator = new RoomOwnershipValidator(_sessionManager);
            _componentRegistry = new RoomComponentRegistry();
            _roomAssembler = new ServerRoomAssembler();
            _idempotentCache = new IdempotentCache(
                config.IdempotentTtlSeconds,
                config.IdempotentCleanupIntervalSeconds);

            _roomManager = new GlobalRoomManager(
                _sessionManager,
                _roomAssembler,
                _componentRegistry,
                config.RoomEmptyTimeoutSeconds);

            _sendCoordinator = new ServerSendCoordinator(_mirrorAdapter, _roomManager);
            _globalSender = new ServerGlobalMessageSender(
                _sendCoordinator,
                _messageRegistry,
                _serializer,
                _sessionManager);

            _roomSender = new ServerRoomMessageSender(
                _sendCoordinator,
                _messageRegistry,
                _serializer,
                _sessionManager,
                _roomManager);

            _networkEntry = new ServerNetworkEntry(
                _messageRegistry,
                _serializer,
                _globalRouter,
                _ownershipValidator,
                _sessionManager);

            if (!ValidateCoreInfrastructure())
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：物体 {name} 的核心基础设施装配不完整。");
                return;
            }

            _ownershipValidator.BindRoomDispatchDelegate(_roomManager.RouteRoomMessage);

            _mirrorAdapter.Initialize(_serializer);
            _networkEntry.BindToAdapter(_mirrorAdapter);
            _mirrorAdapter.OnClientConnected += OnClientConnected;
            _mirrorAdapter.OnClientDisconnected += OnClientDisconnected;

            AssembleGlobalModules();
            if (!ValidateGlobalModules())
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：物体 {name} 的全局模块装配不完整。");
                return;
            }

            RegisterAllHandles();

            _mirrorAdapter.StartListening();
            _isInitialized = true;

            Debug.Log($"[GlobalInfrastructure] 服务端装配完成，开始监听，物体={name}。");
        }

        /// <summary>
        /// 构建本端协议注册表。
        /// 当前默认扫描 Shared 协议程序集。
        /// </summary>
        private MessageRegistry BuildMessageRegistry()
        {
            var assemblies = new List<Assembly>();
            Assembly sharedAssembly = typeof(StellarNet.Shared.Protocol.C2SGlobalMessage).Assembly;
            if (sharedAssembly == null)
            {
                Debug.LogError($"[GlobalInfrastructure] BuildMessageRegistry 失败：物体 {name} 无法获取 Shared 协议程序集。");
                return null;
            }

            assemblies.Add(sharedAssembly);
            return MessageRegistry.Build(assemblies);
        }

        /// <summary>
        /// 校验核心基础设施装配结果，发现任意关键对象为空时立即阻断启动。
        /// </summary>
        private bool ValidateCoreInfrastructure()
        {
            if (_sessionManager == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_sessionManager 为 null，物体={name}。");
                return false;
            }

            if (_globalEventBus == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_globalEventBus 为 null，物体={name}。");
                return false;
            }

            if (_globalServiceLocator == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_globalServiceLocator 为 null，物体={name}。");
                return false;
            }

            if (_globalRouter == null || _globalRegistrar == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：全局路由器或注册器为空，物体={name}。");
                return false;
            }

            if (_ownershipValidator == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_ownershipValidator 为 null，物体={name}。");
                return false;
            }

            if (_componentRegistry == null || _roomAssembler == null || _roomManager == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：房间基础设施缺失，物体={name}。");
                return false;
            }

            if (_idempotentCache == null)
            {
                Debug.LogError(
                    $"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_idempotentCache 为 null，物体={name}。");
                return false;
            }

            if (_sendCoordinator == null || _globalSender == null || _roomSender == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：发送链装配缺失，物体={name}。");
                return false;
            }

            if (_networkEntry == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateCoreInfrastructure 失败：_networkEntry 为 null，物体={name}。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 装配所有出厂全局模块，将 Model 与 Handle 创建并注册到 GlobalScopeServiceLocator。
        /// </summary>
        private void AssembleGlobalModules()
        {
            _userModel = new UserModel();
            _userHandle = new UserHandle(_sessionManager, _userModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<UserHandle>(_userHandle);

            _reconnectModel = new StellarNet.Server.GlobalModules.Reconnect.ReconnectModel();
            _reconnectHandle = new StellarNet.Server.GlobalModules.Reconnect.ReconnectHandle(
                _sessionManager,
                _roomManager,
                _reconnectModel,
                _globalSender,
                _globalRegistrar);
            _globalServiceLocator.Register<StellarNet.Server.GlobalModules.Reconnect.ReconnectHandle>(_reconnectHandle);

            _roomDispatcherModel = new RoomDispatcherModel();
            _roomDispatcherHandle = new RoomDispatcherHandle(
                _sessionManager,
                _roomManager,
                _roomDispatcherModel,
                _idempotentCache,
                _globalSender,
                _roomSender,
                _globalRegistrar);
            _globalServiceLocator.Register<RoomDispatcherHandle>(_roomDispatcherHandle);

            _replayModel = new ReplayModel();
            _replayHandle = new ReplayHandle(_sessionManager, _replayModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<ReplayHandle>(_replayHandle);

            _announcementModel = new AnnouncementModel();
            _announcementHandle = new AnnouncementHandle(
                _sessionManager,
                _announcementModel,
                _globalSender,
                _globalRegistrar);
            _globalServiceLocator.Register<AnnouncementHandle>(_announcementHandle);

            _lobbyChatModel = new LobbyChatModel();
            _lobbyChatHandle = new LobbyChatHandle(
                _sessionManager,
                _lobbyChatModel,
                _globalSender,
                _globalRegistrar);
            _globalServiceLocator.Register<LobbyChatHandle>(_lobbyChatHandle);
        }

        /// <summary>
        /// 校验全局模块装配结果，避免构造器内部只打印错误但外层仍继续运行。
        /// </summary>
        private bool ValidateGlobalModules()
        {
            if (_userModel == null || _userHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：User 模块装配缺失，物体={name}。");
                return false;
            }

            if (_reconnectModel == null || _reconnectHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：Reconnect 模块装配缺失，物体={name}。");
                return false;
            }

            if (_roomDispatcherModel == null || _roomDispatcherHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：RoomDispatcher 模块装配缺失，物体={name}。");
                return false;
            }

            if (_replayModel == null || _replayHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：Replay 模块装配缺失，物体={name}。");
                return false;
            }

            if (_announcementModel == null || _announcementHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：Announcement 模块装配缺失，物体={name}。");
                return false;
            }

            if (_lobbyChatModel == null || _lobbyChatHandle == null)
            {
                Debug.LogError($"[GlobalInfrastructure] ValidateGlobalModules 失败：LobbyChat 模块装配缺失，物体={name}。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 调用所有 Handle 的 RegisterAll()，完成协议处理委托注册。
        /// 必须在所有 Handle 构造完成后统一调用，不允许在构造函数中自行注册。
        /// </summary>
        private void RegisterAllHandles()
        {
            _userHandle.RegisterAll();
            _reconnectHandle.RegisterAll();
            _roomDispatcherHandle.RegisterAll();
            _replayHandle.RegisterAll();
            _announcementHandle.RegisterAll();
            _lobbyChatHandle.RegisterAll();
        }

        private void OnClientConnected(StellarNet.Shared.Identity.ConnectionId connectionId)
        {
            Debug.Log($"[GlobalInfrastructure] 客户端连接建立，ConnectionId={connectionId}。");
        }

        private void OnClientDisconnected(StellarNet.Shared.Identity.ConnectionId connectionId)
        {
            Debug.Log($"[GlobalInfrastructure] 客户端连接断开，ConnectionId={connectionId}。");
            _sessionManager.OnConnectionDisconnected(connectionId);
        }

        /// <summary>
        /// 关停所有服务端基础设施，确保所有房间与录制器正常收尾。
        /// 执行顺序：注销 Handle → 停止监听 → 销毁所有房间 → 清理事件总线与服务定位器。
        /// </summary>
        private void Shutdown()
        {
            if (!_isInitialized)
            {
                return;
            }

            _isInitialized = false;
            Debug.Log($"[GlobalInfrastructure] 开始关停服务端，物体={name}。");

            _userHandle?.UnregisterAll();
            _reconnectHandle?.UnregisterAll();
            _roomDispatcherHandle?.UnregisterAll();
            _replayHandle?.UnregisterAll();
            _announcementHandle?.UnregisterAll();
            _lobbyChatHandle?.UnregisterAll();

            if (_mirrorAdapter != null)
            {
                _networkEntry?.UnbindFromAdapter(_mirrorAdapter);
                _mirrorAdapter.OnClientConnected -= OnClientConnected;
                _mirrorAdapter.OnClientDisconnected -= OnClientDisconnected;
                _mirrorAdapter.StopListening();
            }

            _roomManager?.DestroyAllRooms();
            _globalEventBus?.Clear();
            _globalServiceLocator?.Clear();

            Debug.Log($"[GlobalInfrastructure] 服务端关停完成，物体={name}。");
        }
    }
}