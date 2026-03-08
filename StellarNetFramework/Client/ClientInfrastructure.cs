using System.Collections.Generic;
using System.Reflection;
using StellarNet.Client.Adapter;
using StellarNet.Client.Config;
using StellarNet.Client.GlobalModules.LobbyChat;
using StellarNet.Client.GlobalModules.Reconnect;
using StellarNet.Client.GlobalModules.Replay;
using StellarNet.Client.GlobalModules.RoomDispatcher;
using StellarNet.Client.GlobalModules.User;
using StellarNet.Client.Network;
using StellarNet.Client.Sender;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client
{
    /// <summary>
    /// 客户端全局装配入口，是整个客户端框架的唯一启动点与生命周期宿主。
    /// 负责按正确依赖顺序创建所有客户端基础设施对象、完成依赖注入、绑定事件与发起连接。
    /// 持有所有顶层对象的强引用，防止 GC 提前回收。
    /// Tick 驱动由 Unity MonoBehaviour Update 调用，不使用独立线程。
    /// Shutdown 流程由 OnApplicationQuit 触发，确保连接正常断开与资源释放。
    /// 此类不承担任何业务逻辑，只负责装配与生命周期管理。
    /// </summary>
    public sealed class ClientInfrastructure : MonoBehaviour
    {
        [Header("配置文件路径")] [SerializeField] private string _configFilePath = "Config/ClientNetConfig.json";

        [Header("Mirror 客户端适配器引用")] [SerializeField]
        private MirrorClientAdapter _mirrorAdapter;

        private ClientNetConfigManager _configManager;
        private NewtonsoftJsonSerializer _serializer;
        private MessageRegistry _messageRegistry;
        private ClientSessionContext _sessionContext;
        private ClientGlobalMessageRouter _globalRouter;
        private ClientRoomMessageRouter _roomRouter;
        private ClientGlobalMessageRegistrar _globalRegistrar;
        private ClientNetworkEntry _networkEntry;
        private ClientGlobalMessageSender _globalSender;
        private ClientRoomMessageSender _roomSender;

        private ClientUserModel _userModel;
        private ClientUserHandle _userHandle;
        private ClientReconnectModel _reconnectModel;
        private ClientReconnectHandle _reconnectHandle;
        private ClientRoomDispatcherModel _roomDispatcherModel;
        private ClientRoomDispatcherHandle _roomDispatcherHandle;
        private ClientReplayModel _replayModel;
        private ClientReplayHandle _replayHandle;
        private ClientLobbyChatModel _lobbyChatModel;
        private ClientLobbyChatHandle _lobbyChatHandle;

        private bool _isInitialized;

        public ClientGlobalMessageSender GlobalSender => _globalSender;
        public ClientRoomMessageSender RoomSender => _roomSender;
        public ClientSessionContext SessionContext => _sessionContext;
        public ClientUserHandle UserHandle => _userHandle;
        public ClientReconnectHandle ReconnectHandle => _reconnectHandle;
        public ClientRoomDispatcherHandle RoomDispatcherHandle => _roomDispatcherHandle;
        public ClientReplayHandle ReplayHandle => _replayHandle;
        public ClientLobbyChatHandle LobbyChatHandle => _lobbyChatHandle;
        public ClientRoomMessageRouter RoomRouter => _roomRouter;

        private void Awake()
        {
            if (_mirrorAdapter == null)
            {
                Debug.LogError($"[ClientInfrastructure] Awake 失败：物体 {name} 未挂载 MirrorClientAdapter，客户端无法启动。");
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
            _reconnectHandle?.Tick(deltaTime);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        /// <summary>
        /// 按正确依赖顺序完成所有客户端基础设施对象的创建、注入与绑定。
        /// 任一关键依赖创建失败时输出 Error 并阻止后续装配，防止产生半初始化状态。
        /// </summary>
        private void Initialize()
        {
            _configManager = new ClientNetConfigManager(_configFilePath);
            var config = _configManager.Current;
            if (config == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的 ClientNetConfig 为空。");
                return;
            }

            _serializer = new NewtonsoftJsonSerializer();
            _messageRegistry = BuildMessageRegistry();
            _sessionContext = new ClientSessionContext();
            _globalRouter = new ClientGlobalMessageRouter();
            _roomRouter = new ClientRoomMessageRouter();
            _globalRegistrar = new ClientGlobalMessageRegistrar(_globalRouter);

            if (_serializer == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的序列化器创建失败。");
                return;
            }

            if (_messageRegistry == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的 MessageRegistry 构建失败。");
                return;
            }

            if (_sessionContext == null || _globalRouter == null || _roomRouter == null || _globalRegistrar == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的基础上下文或路由器创建失败。");
                return;
            }

            _networkEntry = new ClientNetworkEntry(
                _messageRegistry,
                _serializer,
                _globalRouter,
                _roomRouter,
                _sessionContext);

            _globalSender = new ClientGlobalMessageSender(
                _mirrorAdapter,
                _messageRegistry,
                _serializer,
                _sessionContext);

            _roomSender = new ClientRoomMessageSender(
                _mirrorAdapter,
                _messageRegistry,
                _serializer,
                _sessionContext);

            if (!ValidateCoreInfrastructure())
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的核心基础设施装配不完整。");
                return;
            }

            AssembleGlobalModules(config);
            if (!ValidateGlobalModules())
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的全局模块装配不完整，已阻断启动。");
                return;
            }

            RegisterAllHandles();

            _mirrorAdapter.Initialize(_serializer);
            _networkEntry.BindToAdapter(_mirrorAdapter);
            _mirrorAdapter.Connect(config.ServerAddress, config.ServerPort);

            _isInitialized = true;
            Debug.Log($"[ClientInfrastructure] 客户端装配完成，正在连接 {config.ServerAddress}:{config.ServerPort}，物体={name}。");
        }

        /// <summary>
        /// 构建本端协议注册表。
        /// 当前默认扫描 Shared 协议程序集。
        /// </summary>
        private MessageRegistry BuildMessageRegistry()
        {
            var assemblies = new List<Assembly>();
            Assembly sharedAssembly = typeof(C2SGlobalMessage).Assembly;
            if (sharedAssembly == null)
            {
                Debug.LogError($"[ClientInfrastructure] BuildMessageRegistry 失败：物体 {name} 无法获取 Shared 协议程序集。");
                return null;
            }

            assemblies.Add(sharedAssembly);
            return MessageRegistry.Build(assemblies);
        }

        /// <summary>
        /// 校验核心基础设施装配结果。
        /// 这里做统一拦截，是为了避免构造器内部只打印错误但外层仍继续运行。
        /// </summary>
        private bool ValidateCoreInfrastructure()
        {
            if (_networkEntry == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_networkEntry 为 null，物体={name}。");
                return false;
            }

            if (_globalSender == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_globalSender 为 null，物体={name}。");
                return false;
            }

            if (_roomSender == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_roomSender 为 null，物体={name}。");
                return false;
            }

            if (!_networkEntry.IsAvailable)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_networkEntry 内部依赖未正确初始化，物体={name}。");
                return false;
            }

            if (!_globalSender.IsAvailable)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_globalSender 内部依赖未正确初始化，物体={name}。");
                return false;
            }

            if (!_roomSender.IsAvailable)
            {
                Debug.LogError(
                    $"[ClientInfrastructure] ValidateCoreInfrastructure 失败：_roomSender 内部依赖未正确初始化，物体={name}。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 装配所有出厂客户端全局模块。
        /// </summary>
        private void AssembleGlobalModules(ClientNetConfig config)
        {
            _userModel = new ClientUserModel();
            _userHandle = new ClientUserHandle(_userModel, _sessionContext, _globalRegistrar);

            _reconnectModel = new ClientReconnectModel(config.ReconnectMaxAttempts);
            _reconnectHandle = new ClientReconnectHandle(
                _reconnectModel,
                _sessionContext,
                _globalRegistrar,
                _mirrorAdapter,
                _globalSender,
                config.ServerAddress,
                config.ServerPort,
                config.ReconnectIntervalSeconds);

            _roomDispatcherModel = new ClientRoomDispatcherModel();
            _roomDispatcherHandle = new ClientRoomDispatcherHandle(
                _roomDispatcherModel,
                _sessionContext,
                _globalRegistrar);

            _replayModel = new ClientReplayModel();
            _replayHandle = new ClientReplayHandle(_replayModel, _globalRegistrar, _globalSender);

            _lobbyChatModel = new ClientLobbyChatModel();
            _lobbyChatHandle = new ClientLobbyChatHandle(_lobbyChatModel, _globalRegistrar);
        }

        /// <summary>
        /// 校验全局模块装配结果。
        /// </summary>
        private bool ValidateGlobalModules()
        {
            if (_userModel == null || _userHandle == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateGlobalModules 失败：User 模块装配缺失，物体={name}。");
                return false;
            }

            if (_reconnectModel == null || _reconnectHandle == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateGlobalModules 失败：Reconnect 模块装配缺失，物体={name}。");
                return false;
            }

            if (_roomDispatcherModel == null || _roomDispatcherHandle == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateGlobalModules 失败：RoomDispatcher 模块装配缺失，物体={name}。");
                return false;
            }

            if (_replayModel == null || _replayHandle == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateGlobalModules 失败：Replay 模块装配缺失，物体={name}。");
                return false;
            }

            if (_lobbyChatModel == null || _lobbyChatHandle == null)
            {
                Debug.LogError($"[ClientInfrastructure] ValidateGlobalModules 失败：LobbyChat 模块装配缺失，物体={name}。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 调用所有 Handle 的 RegisterAll()，完成协议处理委托注册。
        /// </summary>
        private void RegisterAllHandles()
        {
            _userHandle.RegisterAll();
            _reconnectHandle.RegisterAll();
            _roomDispatcherHandle.RegisterAll();
            _replayHandle.RegisterAll();
            _lobbyChatHandle.RegisterAll();
        }

        /// <summary>
        /// 关停所有客户端基础设施。
        /// </summary>
        private void Shutdown()
        {
            if (!_isInitialized)
            {
                return;
            }

            _isInitialized = false;
            Debug.Log($"[ClientInfrastructure] 开始关停客户端，物体={name}。");

            _userHandle?.UnregisterAll();
            _reconnectHandle?.UnregisterAll();
            _roomDispatcherHandle?.UnregisterAll();
            _replayHandle?.UnregisterAll();
            _lobbyChatHandle?.UnregisterAll();

            if (_mirrorAdapter != null)
            {
                _networkEntry?.UnbindFromAdapter(_mirrorAdapter);
                _mirrorAdapter.Disconnect();
            }

            Debug.Log($"[ClientInfrastructure] 客户端关停完成，物体={name}。");
        }
    }
}