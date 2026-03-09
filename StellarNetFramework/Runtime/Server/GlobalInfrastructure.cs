using System.Collections.Generic;
using System.IO;
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
using StellarNet.Server.Room.BuiltIn; // [新增] 引入内置组件命名空间
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
    /// </summary>
    public sealed class GlobalInfrastructure : MonoBehaviour
    {
        [Header("配置加载路径 (支持 @StreamingAssets 等标记)")]
        public string ConfigLoadPath;

        [Header("Mirror 适配器引用")] [SerializeField]
        private MirrorServerAdapter _mirrorAdapter;

        // 顶层对象强引用
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
            if (!_isInitialized) return;
            float deltaTime = Time.deltaTime;
            _sessionManager?.Tick();
            _idempotentCache?.Tick();
            _roomManager?.Tick(deltaTime);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void Initialize()
        {
            // 解析路径标记并拼接文件名
            string folderPath = ResolveConfigPath(ConfigLoadPath);
            string fullJsonPath = Path.Combine(folderPath, "NetConfig.json");

            _configManager = new NetConfigManager(fullJsonPath);
            var config = _configManager.Current;
            if (config == null)
            {
                Debug.LogError($"[GlobalInfrastructure] Initialize 失败：无法从路径加载配置: {fullJsonPath}");
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

            // 1. 创建注册表
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

            // 2. 创建发送器
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

            // [关键修复] 3. 注册内置房间组件工厂
            // 必须在 _globalSender, _roomSender, _sessionManager 创建之后调用
            RegisterBuiltInRoomComponents();

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
            Debug.Log($"[GlobalInfrastructure] 服务端装配完成，开始监听，配置源: {fullJsonPath}");
        }

        /// <summary>
        /// 注册内置房间组件
        /// </summary>
        private void RegisterBuiltInRoomComponents()
        {
            // 注册 RoomBaseSettings 组件工厂
            // 使用 Lambda 表达式注入依赖，实现控制反转
            _componentRegistry.Register(
                ServerRoomBaseSettingsHandle.StableComponentId,
                roomInstance => new ServerRoomBaseSettingsHandle(
                    _globalSender,
                    _roomSender,
                    _sessionManager,
                    _roomManager
                )
            );

            Debug.Log("[GlobalInfrastructure] 内置房间组件注册完成。");
        }

        private string ResolveConfigPath(string rawPath)
        {
            if (string.IsNullOrEmpty(rawPath))
                return Path.Combine(Application.streamingAssetsPath, "ServerNetConfig");

            string path = rawPath.Replace("\\", "/");
            if (path.StartsWith("@StreamingAssets"))
            {
                return path.Replace("@StreamingAssets", Application.streamingAssetsPath);
            }
            else if (path.StartsWith("@PersistentData"))
            {
                return path.Replace("@PersistentData", Application.persistentDataPath);
            }
            else if (path.StartsWith("@DataPath"))
            {
                return path.Replace("@DataPath", Application.dataPath);
            }

            return path;
        }

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

        private bool ValidateCoreInfrastructure()
        {
            if (_sessionManager == null) return false;
            if (_globalEventBus == null) return false;
            if (_globalServiceLocator == null) return false;
            if (_globalRouter == null || _globalRegistrar == null) return false;
            if (_ownershipValidator == null) return false;
            if (_componentRegistry == null || _roomAssembler == null || _roomManager == null) return false;
            if (_idempotentCache == null) return false;
            if (_sendCoordinator == null || _globalSender == null || _roomSender == null) return false;
            if (_networkEntry == null) return false;
            return true;
        }

        private void AssembleGlobalModules()
        {
            _userModel = new UserModel();
            _userHandle = new UserHandle(_sessionManager, _userModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<UserHandle>(_userHandle);

            _reconnectModel = new StellarNet.Server.GlobalModules.Reconnect.ReconnectModel();
            _reconnectHandle = new StellarNet.Server.GlobalModules.Reconnect.ReconnectHandle(
                _sessionManager, _roomManager, _reconnectModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<StellarNet.Server.GlobalModules.Reconnect.ReconnectHandle>(_reconnectHandle);

            _roomDispatcherModel = new RoomDispatcherModel();
            _roomDispatcherHandle = new RoomDispatcherHandle(
                _sessionManager, _roomManager, _roomDispatcherModel, _idempotentCache,
                _globalSender, _roomSender, _globalRegistrar);
            _globalServiceLocator.Register<RoomDispatcherHandle>(_roomDispatcherHandle);

            _replayModel = new ReplayModel();
            _replayHandle = new ReplayHandle(_sessionManager, _replayModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<ReplayHandle>(_replayHandle);

            _announcementModel = new AnnouncementModel();
            _announcementHandle = new AnnouncementHandle(
                _sessionManager, _announcementModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<AnnouncementHandle>(_announcementHandle);

            _lobbyChatModel = new LobbyChatModel();
            _lobbyChatHandle = new LobbyChatHandle(
                _sessionManager, _lobbyChatModel, _globalSender, _globalRegistrar);
            _globalServiceLocator.Register<LobbyChatHandle>(_lobbyChatHandle);
        }

        private bool ValidateGlobalModules()
        {
            if (_userModel == null || _userHandle == null) return false;
            if (_reconnectModel == null || _reconnectHandle == null) return false;
            if (_roomDispatcherModel == null || _roomDispatcherHandle == null) return false;
            if (_replayModel == null || _replayHandle == null) return false;
            if (_announcementModel == null || _announcementHandle == null) return false;
            if (_lobbyChatModel == null || _lobbyChatHandle == null) return false;
            return true;
        }

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

        private void Shutdown()
        {
            if (!_isInitialized) return;
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