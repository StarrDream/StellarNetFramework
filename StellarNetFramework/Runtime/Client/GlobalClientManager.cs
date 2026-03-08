using StellarNet.Client.GlobalModules.Replay;
using StellarNet.Client.Room;
using StellarNet.Client.Session;
using StellarNet.Client.State;
using UnityEngine;

namespace StellarNet.Client
{
    /// <summary>
    /// 客户端全局运行时协调门面。
    /// 负责持有客户端主状态机、底层连接状态，并协调合法的状态迁移。
    /// 实现 IClientStateProvider 接口供底层协议过滤器查询当前状态，解耦循环依赖。
    /// 它是客户端房间创建、恢复与销毁流程的唯一协调入口。
    /// </summary>
    public sealed class GlobalClientManager : IClientStateProvider
    {
        public ClientAppState CurrentState { get; private set; }
        public bool IsConnected { get; private set; }

        // 客户端全局门面直接字段持有回放控制器
        public ClientReplayPlaybackController ReplayController { get; private set; }

        /// <summary>
        /// 当前在线房间实例。
        /// 仅在 InRoom 状态下有效，InLobby/InReplay/Disconnected 下为 null。
        /// </summary>
        public ClientRoomInstance CurrentRoom { get; private set; }

        private readonly ClientSessionContext _sessionContext;

        public GlobalClientManager(ClientSessionContext sessionContext)
        {
            if (sessionContext == null)
            {
                Debug.LogError("[GlobalClientManager] 构造失败：sessionContext 为 null。");
                return;
            }

            _sessionContext = sessionContext;
            CurrentState = ClientAppState.Disconnected;
            IsConnected = false;
        }

        /// <summary>
        /// 注入回放控制器，由 ClientInfrastructure 在装配阶段调用。
        /// </summary>
        public void InitializeReplayController(ClientReplayPlaybackController replayController)
        {
            if (replayController == null)
            {
                Debug.LogError("[GlobalClientManager] InitializeReplayController 失败：replayController 为 null。");
                return;
            }

            ReplayController = replayController;
        }

        /// <summary>
        /// 设置当前在线房间实例。
        /// 必须在 TransitionToRoom 之前调用。
        /// </summary>
        public void SetCurrentRoom(ClientRoomInstance room)
        {
            if (room == null)
            {
                Debug.LogError("[GlobalClientManager] SetCurrentRoom 失败：room 为 null。");
                return;
            }

            if (CurrentRoom != null && CurrentRoom != room)
            {
                Debug.LogWarning(
                    $"[GlobalClientManager] SetCurrentRoom 警告：覆盖了旧房间实例 {CurrentRoom.RoomId} -> {room.RoomId}，请确认旧房间已销毁。");
            }

            CurrentRoom = room;
        }

        /// <summary>
        /// 清理当前在线房间实例。
        /// 在退出房间或断线时调用。
        /// </summary>
        public void ClearCurrentRoom()
        {
            if (CurrentRoom != null)
            {
                CurrentRoom.Destroy();
                CurrentRoom = null;
            }
        }

        public void OnConnectedToServer()
        {
            IsConnected = true;
            if (CurrentState == ClientAppState.Disconnected)
            {
                CurrentState = ClientAppState.Authenticating;
                Debug.Log("[GlobalClientManager] 底层连接建立，状态切换为 Authenticating。");
            }
        }

        public void OnDisconnectedFromServer()
        {
            IsConnected = false;
            if (CurrentState != ClientAppState.InReplay)
            {
                // 在线模式下断线，强制清理房间
                ClearCurrentRoom();
                CurrentState = ClientAppState.Disconnected;
                Debug.Log("[GlobalClientManager] 底层连接断开，状态切换为 Disconnected，已清理在线房间。");
            }
            else
            {
                Debug.Log("[GlobalClientManager] 底层连接断开，当前处于回放模式，保持 InReplay 状态。");
            }
        }

        public void TransitionToLobby()
        {
            if (CurrentState == ClientAppState.Authenticating ||
                CurrentState == ClientAppState.InRoom ||
                CurrentState == ClientAppState.InReplay)
            {
                // 确保退出房间状态时清理实例
                if (CurrentState == ClientAppState.InRoom)
                {
                    ClearCurrentRoom();
                }

                CurrentState = ClientAppState.InLobby;
                Debug.Log("[GlobalClientManager] 状态切换为 InLobby。");
            }
            else
            {
                Debug.LogError($"[GlobalClientManager] 非法状态迁移：无法从 {CurrentState} 切换到 InLobby。");
            }
        }

        public void TransitionToRoom()
        {
            if (CurrentState == ClientAppState.InLobby ||
                CurrentState == ClientAppState.Authenticating)
            {
                if (CurrentRoom == null)
                {
                    Debug.LogError(
                        "[GlobalClientManager] TransitionToRoom 失败：CurrentRoom 为 null，请先调用 SetCurrentRoom 装配房间。");
                    return;
                }

                CurrentState = ClientAppState.InRoom;
                Debug.Log($"[GlobalClientManager] 状态切换为 InRoom，RoomId={CurrentRoom.RoomId}。");
            }
            else
            {
                Debug.LogError($"[GlobalClientManager] 非法状态迁移：无法从 {CurrentState} 切换到 InRoom。");
            }
        }

        public void TransitionToReplay()
        {
            if (CurrentState == ClientAppState.InLobby)
            {
                // 进入回放前确保无在线房间残留
                ClearCurrentRoom();
                CurrentState = ClientAppState.InReplay;
                Debug.Log("[GlobalClientManager] 状态切换为 InReplay。");
            }
            else
            {
                Debug.LogError($"[GlobalClientManager] 非法状态迁移：只能从 InLobby 切换到 InReplay，当前状态={CurrentState}。");
            }
        }

        public void TransitionToDisconnected()
        {
            ClearCurrentRoom();
            CurrentState = ClientAppState.Disconnected;
            Debug.Log("[GlobalClientManager] 状态切换为 Disconnected。");
        }
    }
}