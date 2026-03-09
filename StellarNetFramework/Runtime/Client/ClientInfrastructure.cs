// ════════════════════════════════════════════════════════════════
// 文件：ClientInfrastructure.cs
// 路径：Assets/StellarNetFramework/Runtime/Client/ClientInfrastructure.cs
// 职责：客户端基础设施装配与生命周期管理。
//       修正：OnCreateRoomSucceeded 不再使用硬编码默认组件，
//       而是使用服务端下发的组件清单进行装配，确保“所见即所得”。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using StellarNet.Client.Adapter;
using StellarNet.Client.Config;
using StellarNet.Client.GlobalModules.LobbyChat;
using StellarNet.Client.GlobalModules.Reconnect;
using StellarNet.Client.GlobalModules.Replay;
using StellarNet.Client.GlobalModules.RoomDispatcher;
using StellarNet.Client.GlobalModules.User;
using StellarNet.Client.Network;
using StellarNet.Client.Room;
using StellarNet.Client.Room.Components;
using StellarNet.Client.Sender;
using StellarNet.Client.Session;
using StellarNet.Client.State;
using StellarNet.Shared.Protocol;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client
{
    public sealed class ClientInfrastructure : MonoBehaviour
    {
        [Header("配置加载路径 (支持 @StreamingAssets 等标记)")]
        public string ConfigLoadPath;

        [Header("Mirror 客户端适配器引用")] [SerializeField]
        private MirrorClientAdapter _mirrorAdapter;

        private ClientNetConfigManager _configManager;
        private NewtonsoftJsonSerializer _serializer;
        private MessageRegistry _messageRegistry;
        private ClientSessionContext _sessionContext;
        private GlobalClientManager _globalClientManager;
        private ClientStateProtocolFilter _protocolFilter;
        private ClientGlobalMessageRouter _globalRouter;
        private ClientGlobalMessageRegistrar _globalRegistrar;
        private ClientNetworkEntry _networkEntry;
        private ClientGlobalMessageSender _globalSender;
        private ClientRoomMessageSender _roomSender;
        private ClientRoomComponentRegistry _clientComponentRegistry;

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
        public GlobalClientManager GlobalClientManager => _globalClientManager;
        public ClientUserHandle UserHandle => _userHandle;
        public ClientReconnectHandle ReconnectHandle => _reconnectHandle;
        public ClientRoomDispatcherHandle RoomDispatcherHandle => _roomDispatcherHandle;
        public ClientReplayHandle ReplayHandle => _replayHandle;
        public ClientLobbyChatHandle LobbyChatHandle => _lobbyChatHandle;

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
            if (!_isInitialized) return;
            float deltaTime = Time.deltaTime;
            _reconnectHandle?.Tick(deltaTime);
            _globalClientManager?.CurrentRoom?.Tick(deltaTime);
            _globalClientManager?.ReplayController?.Tick(deltaTime);
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void Initialize()
        {
            // 解析路径标记并拼接文件名
            string folderPath = ResolveConfigPath(ConfigLoadPath);
            string fullJsonPath = Path.Combine(folderPath, "ClientNetConfig.json");

            _configManager = new ClientNetConfigManager(fullJsonPath);
            var config = _configManager.Current;
            if (config == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：无法从路径加载配置: {fullJsonPath}");
                return;
            }

            _serializer = new NewtonsoftJsonSerializer();
            _messageRegistry = BuildMessageRegistry();
            _sessionContext = new ClientSessionContext();
            _globalClientManager = new GlobalClientManager(_sessionContext);
            _protocolFilter = new ClientStateProtocolFilter(_globalClientManager);
            _globalRouter = new ClientGlobalMessageRouter();
            _globalRegistrar = new ClientGlobalMessageRegistrar(_globalRouter);
            _clientComponentRegistry = new ClientRoomComponentRegistry();

            RegisterClientComponents();

            if (_serializer == null || _messageRegistry == null || _sessionContext == null ||
                _globalRouter == null || _globalRegistrar == null || _protocolFilter == null)
            {
                Debug.LogError($"[ClientInfrastructure] Initialize 失败：物体 {name} 的基础上下文或路由器创建失败。");
                return;
            }

            _networkEntry = new ClientNetworkEntry(
                _messageRegistry,
                _serializer,
                _globalRouter,
                _globalClientManager,
                _sessionContext,
                _protocolFilter);

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
            BindStateTransitions();

            _mirrorAdapter.OnConnectedToServer += _globalClientManager.OnConnectedToServer;
            _mirrorAdapter.OnDisconnectedFromServer += _globalClientManager.OnDisconnectedFromServer;

            _mirrorAdapter.Initialize(_serializer);
            _networkEntry.BindToAdapter(_mirrorAdapter);

            _mirrorAdapter.Connect(config.ServerAddress, config.ServerPort);

            _isInitialized = true;
            Debug.Log(
                $"[ClientInfrastructure] 客户端装配完成，正在连接 {config.ServerAddress}:{config.ServerPort}，配置源: {fullJsonPath}");
        }

        private string ResolveConfigPath(string rawPath)
        {
            if (string.IsNullOrEmpty(rawPath))
                return Path.Combine(Application.streamingAssetsPath, "ClientNetConfig");

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
            Assembly sharedAssembly = typeof(C2SGlobalMessage).Assembly;
            if (sharedAssembly == null)
            {
                Debug.LogError($"[ClientInfrastructure] BuildMessageRegistry 失败：物体 {name} 无法获取 Shared 协议程序集。");
                return null;
            }

            assemblies.Add(sharedAssembly);
            return MessageRegistry.Build(assemblies);
        }

        private bool ValidateCoreInfrastructure()
        {
            if (_networkEntry == null || _globalSender == null || _roomSender == null) return false;
            if (!_networkEntry.IsAvailable || !_globalSender.IsAvailable || !_roomSender.IsAvailable) return false;
            return true;
        }

        private void AssembleGlobalModules(ClientNetConfig config)
        {
            _userModel = new ClientUserModel();
            _userHandle = new ClientUserHandle(_userModel, _sessionContext, _globalRegistrar);

            _reconnectModel = new ClientReconnectModel(config.ReconnectMaxAttempts);
            _reconnectHandle = new ClientReconnectHandle(
                _reconnectModel, _sessionContext, _globalRegistrar, _mirrorAdapter, _globalSender,
                config.ServerAddress, config.ServerPort, config.ReconnectIntervalSeconds);

            _roomDispatcherModel = new ClientRoomDispatcherModel();
            _roomDispatcherHandle =
                new ClientRoomDispatcherHandle(_roomDispatcherModel, _sessionContext, _globalRegistrar);

            _replayModel = new ClientReplayModel();
            _replayHandle = new ClientReplayHandle(_replayModel, _globalRegistrar, _globalSender);

            _lobbyChatModel = new ClientLobbyChatModel();
            _lobbyChatHandle = new ClientLobbyChatHandle(_lobbyChatModel, _globalRegistrar);

            var replayController =
                new ClientReplayPlaybackController(_globalClientManager, _messageRegistry, _serializer);
            _globalClientManager.InitializeReplayController(replayController);
        }

        private bool ValidateGlobalModules()
        {
            if (_userModel == null || _userHandle == null) return false;
            if (_reconnectModel == null || _reconnectHandle == null) return false;
            if (_roomDispatcherModel == null || _roomDispatcherHandle == null) return false;
            if (_replayModel == null || _replayHandle == null) return false;
            if (_lobbyChatModel == null || _lobbyChatHandle == null) return false;
            return true;
        }

        private void RegisterAllHandles()
        {
            _userHandle.RegisterAll();
            _reconnectHandle.RegisterAll();
            _roomDispatcherHandle.RegisterAll();
            _replayHandle.RegisterAll();
            _lobbyChatHandle.RegisterAll();
        }

        private void RegisterClientComponents()
        {
            _clientComponentRegistry.Register(
                ClientRoomBaseSettingsHandle.StableComponentId,
                room => new ClientRoomBaseSettingsHandle()
            );
        }

        private void BindStateTransitions()
        {
            _userHandle.OnLoginSuccess += OnLoginSuccess;
            _userHandle.OnKickedOut += OnKickedOut;
            _roomDispatcherHandle.OnCreateRoomSucceeded += OnCreateRoomSucceeded;
            _roomDispatcherHandle.OnJoinRoomSucceeded += OnJoinRoomSucceeded;
            _roomDispatcherHandle.OnLeaveRoomSucceeded += OnLeaveRoomSucceeded;
            _reconnectHandle.OnReconnectSucceeded += OnReconnectSucceeded;
            _reconnectHandle.OnReconnectFailed += OnReconnectFailed;
        }

        private void UnbindStateTransitions()
        {
            if (_userHandle != null)
            {
                _userHandle.OnLoginSuccess -= OnLoginSuccess;
                _userHandle.OnKickedOut -= OnKickedOut;
            }

            if (_roomDispatcherHandle != null)
            {
                _roomDispatcherHandle.OnCreateRoomSucceeded -= OnCreateRoomSucceeded;
                _roomDispatcherHandle.OnJoinRoomSucceeded -= OnJoinRoomSucceeded;
                _roomDispatcherHandle.OnLeaveRoomSucceeded -= OnLeaveRoomSucceeded;
            }

            if (_reconnectHandle != null)
            {
                _reconnectHandle.OnReconnectSucceeded -= OnReconnectSucceeded;
                _reconnectHandle.OnReconnectFailed -= OnReconnectFailed;
            }
        }

        private void OnLoginSuccess(string sessionId) => _globalClientManager.TransitionToLobby();
        private void OnKickedOut(string reason) => _globalClientManager.TransitionToDisconnected();

        private void OnCreateRoomSucceeded(string roomId, string[] components)
        {
            // [关键修改] 不再使用硬编码的 defaultComponents
            // 直接使用服务端下发的 components 列表进行装配
            // 确保创建者与加入者的房间结构完全一致
            if (components == null || components.Length == 0)
            {
                Debug.LogWarning($"[ClientInfrastructure] 建房成功但组件列表为空，将装配空房间。RoomId={roomId}");
            }

            PerformRoomAssembly(roomId, components);
        }

        private void OnJoinRoomSucceeded(string roomId, string[] components)
        {
            PerformRoomAssembly(roomId, components);
        }

        private void PerformRoomAssembly(string roomId, string[] components)
        {
            var room = new ClientRoomInstance(roomId);
            var assembler = new ClientRoomAssembler(_clientComponentRegistry.GetRegistry());

            bool success = assembler.Assemble(room, components);
            if (!success)
            {
                Debug.LogError($"[ClientInfrastructure] 房间装配失败，无法进入房间。RoomId={roomId}");
                return;
            }

            _globalClientManager.SetCurrentRoom(room);
            _globalClientManager.TransitionToRoom();
        }

        private void OnLeaveRoomSucceeded() => _globalClientManager.TransitionToLobby();

        private void OnReconnectSucceeded(string targetState)
        {
            if (targetState == "InRoom") _globalClientManager.TransitionToRoom();
            else _globalClientManager.TransitionToLobby();
        }

        private void OnReconnectFailed(string reason) => _globalClientManager.TransitionToDisconnected();

        private void Shutdown()
        {
            if (!_isInitialized) return;
            _isInitialized = false;

            Debug.Log($"[ClientInfrastructure] 开始关停客户端，物体={name}。");

            UnbindStateTransitions();

            _userHandle?.UnregisterAll();
            _reconnectHandle?.UnregisterAll();
            _roomDispatcherHandle?.UnregisterAll();
            _replayHandle?.UnregisterAll();
            _lobbyChatHandle?.UnregisterAll();

            if (_mirrorAdapter != null)
            {
                _mirrorAdapter.OnConnectedToServer -= _globalClientManager.OnConnectedToServer;
                _mirrorAdapter.OnDisconnectedFromServer -= _globalClientManager.OnDisconnectedFromServer;
                _networkEntry?.UnbindFromAdapter(_mirrorAdapter);
                _mirrorAdapter.Disconnect();
            }

            _globalClientManager?.ClearCurrentRoom();

            Debug.Log($"[ClientInfrastructure] 客户端关停完成，物体={name}。");
        }
    }
}