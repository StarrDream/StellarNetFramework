using System.Collections.Generic;
using System.Globalization;
using StellarNet.Client.Network;
using UnityEngine;

namespace StellarNet.Client.Room
{
    /// <summary>
    /// 客户端房间生命周期状态枚举。
    /// 与服务端隔离，避免客户端代码越界引用 StellarNet.Server 命名空间。
    /// </summary>
    public enum ClientRoomLifecycleState
    {
        Initializing,
        Running,
        Destroying,
        Destroyed
    }

    /// <summary>
    /// 客户端专用的作用域服务定位器。
    /// </summary>
    public sealed class ClientScopeServiceLocator
    {
        private readonly Dictionary<System.Type, object> _services = new Dictionary<System.Type, object>();
        private readonly string _scopeName;

        public ClientScopeServiceLocator(string scopeName)
        {
            _scopeName = scopeName ?? "Unknown";
        }

        public void Register<TService>(TService service) where TService : class
        {
            _services[typeof(TService)] = service;
        }

        public TService Get<TService>() where TService : class
        {
            _services.TryGetValue(typeof(TService), out var s);
            return s as TService;
        }

        public void Unregister<TService>() where TService : class
        {
            _services.Remove(typeof(TService));
        }

        public void Clear()
        {
            _services.Clear();
        }
    }

    /// <summary>
    /// 客户端单个房间的唯一生命周期宿主。
    /// 在线模式与回放模式都必须创建独立的 ClientRoomInstance 实例，保证物理隔离。
    /// 每个实例持有自己独立的 ClientRoomMessageRouter 与 ClientScopeServiceLocator。
    /// </summary>
    public sealed class ClientRoomInstance
    {
        public string RoomId { get; }
        public ClientRoomLifecycleState LifecycleState { get; private set; }

        /// <summary>
        /// 客户端房间内基础路由容器。
        /// 在线模式接收网络下行，回放模式接收本地控制器注入。
        /// </summary>
        public ClientRoomMessageRouter MessageRouter { get; }

        /// <summary>
        /// 客户端房间作用域服务定位器。
        /// </summary>
        public ClientScopeServiceLocator RoomServiceLocator { get; }

        public string CurrentTick { get; set; }

        private readonly List<IClientRoomComponent> _components = new List<IClientRoomComponent>();

        public ClientRoomInstance(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ClientRoomInstance] 构造失败：roomId 为空。");
                return;
            }

            RoomId = roomId;
            LifecycleState = ClientRoomLifecycleState.Initializing;

            MessageRouter = new ClientRoomMessageRouter();
            RoomServiceLocator = new ClientScopeServiceLocator($"ClientRoomScope({roomId})");
        }

        public void MarkRunning()
        {
            if (LifecycleState != ClientRoomLifecycleState.Initializing)
            {
                Debug.LogError($"[ClientRoomInstance] MarkRunning 失败：当前状态不是 Initializing，实际状态={LifecycleState}。");
                return;
            }

            LifecycleState = ClientRoomLifecycleState.Running;
        }

        public void AddComponent(IClientRoomComponent component)
        {
            if (LifecycleState != ClientRoomLifecycleState.Initializing)
            {
                Debug.LogError($"[ClientRoomInstance] AddComponent 失败：只允许在 Initializing 阶段添加组件。");
                return;
            }

            if (component == null) return;
            _components.Add(component);
        }

        public void RemoveComponent(IClientRoomComponent component)
        {
            _components.Remove(component);
        }

        public void Tick(float deltaTime)
        {
            if (LifecycleState != ClientRoomLifecycleState.Running) return;

            for (int i = 0; i < _components.Count; i++)
            {
                _components[i].OnTick(deltaTime);
            }

            CurrentTick = deltaTime.ToString(CultureInfo.InvariantCulture);
        }

        public void Destroy()
        {
            if (LifecycleState == ClientRoomLifecycleState.Destroying ||
                LifecycleState == ClientRoomLifecycleState.Destroyed)
            {
                return;
            }

            LifecycleState = ClientRoomLifecycleState.Destroying;
            Debug.Log($"[ClientRoomInstance] 客户端房间开始销毁，RoomId={RoomId}。");

            // 按装配逆序销毁组件
            for (int i = _components.Count - 1; i >= 0; i--)
            {
                _components[i].OnRoomDestroy();
            }

            MessageRouter.ClearAll();
            RoomServiceLocator.Clear();
            _components.Clear();

            LifecycleState = ClientRoomLifecycleState.Destroyed;
            Debug.Log($"[ClientRoomInstance] 客户端房间销毁完成，RoomId={RoomId}。");
        }
    }

    /// <summary>
    /// 客户端房间业务组件基础接口。
    /// </summary>
    public interface IClientRoomComponent
    {
        void OnTick(float deltaTime);
        void OnRoomDestroy();
    }
}