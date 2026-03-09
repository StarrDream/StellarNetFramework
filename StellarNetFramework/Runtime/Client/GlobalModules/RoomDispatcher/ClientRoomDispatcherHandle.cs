using StellarNet.Client.Network;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 客户端房间调度模块 Handle，处理建房、加房结果，以及被踢、解散通知。
    /// </summary>
    public sealed class ClientRoomDispatcherHandle
    {
        private readonly ClientRoomDispatcherModel _model;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRegistrar _registrar;

        public event System.Action<string, string[]> OnCreateRoomSucceeded;
        public event System.Action<string> OnCreateRoomFailed;
        public event System.Action<string, string[]> OnJoinRoomSucceeded;
        public event System.Action<string> OnJoinRoomFailed;
        public event System.Action OnLeaveRoomSucceeded;
        public event System.Action<RoomBriefInfo[]> OnRoomListReceived;
        public event System.Action<S2C_RoomInfoResult> OnRoomInfoReceived;

        public ClientRoomDispatcherHandle(
            ClientRoomDispatcherModel model,
            ClientSessionContext sessionContext,
            ClientGlobalMessageRegistrar registrar)
        {
            _model = model;
            _sessionContext = sessionContext;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<S2C_CreateRoomResult>(OnS2C_CreateRoomResult)
                .Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
                .Register<S2C_LeaveRoomResult>(OnS2C_LeaveRoomResult)
                .Register<S2C_RoomListResult>(OnS2C_RoomListResult)
                .Register<S2C_RoomInfoResult>(OnS2C_RoomInfoResult)
                // [新增] 注册踢出和解散通知 (Global Domain)
                .Register<S2C_KickedFromRoom>(OnS2C_KickedFromRoom)
                .Register<S2C_RoomDismissed>(OnS2C_RoomDismissed);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_CreateRoomResult>()
                .Unregister<S2C_JoinRoomResult>()
                .Unregister<S2C_LeaveRoomResult>()
                .Unregister<S2C_RoomListResult>()
                .Unregister<S2C_RoomInfoResult>()
                .Unregister<S2C_KickedFromRoom>()
                .Unregister<S2C_RoomDismissed>();
        }

        private void OnS2C_CreateRoomResult(S2C_CreateRoomResult message)
        {
            if (message == null) return;
            if (!message.Success)
            {
                _model.SetCreateFailed(message.FailReason);
                OnCreateRoomFailed?.Invoke(message.FailReason);
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                _model.SetCreateFailed("服务端返回 RoomId 为空");
                OnCreateRoomFailed?.Invoke("服务端返回 RoomId 为空");
                return;
            }

            _sessionContext.SetCurrentRoomId(message.RoomId);
            _model.SetCreateSucceeded();
            OnCreateRoomSucceeded?.Invoke(message.RoomId, message.RoomComponentIds);
        }

        private void OnS2C_JoinRoomResult(S2C_JoinRoomResult message)
        {
            if (message == null) return;
            if (!message.Success)
            {
                _model.SetJoinFailed(message.FailReason);
                OnJoinRoomFailed?.Invoke(message.FailReason);
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                _model.SetJoinFailed("服务端返回 RoomId 为空");
                OnJoinRoomFailed?.Invoke("服务端返回 RoomId 为空");
                return;
            }

            _sessionContext.SetCurrentRoomId(message.RoomId);
            _model.SetJoinSucceeded(message.RoomId, message.RoomComponentIds);
            OnJoinRoomSucceeded?.Invoke(message.RoomId, message.RoomComponentIds);
        }

        private void OnS2C_LeaveRoomResult(S2C_LeaveRoomResult message)
        {
            if (message == null) return;

            // 收到离房结果，清理本地状态
            _sessionContext.ClearCurrentRoomId();
            _model.ClearRoomState();

            OnLeaveRoomSucceeded?.Invoke();
            Debug.Log("[ClientRoomDispatcherHandle] 离房成功，已触发状态回调。");
        }

        private void OnS2C_RoomListResult(S2C_RoomListResult message)
        {
            if (message == null) return;
            Debug.Log($"[ClientRoomDispatcherHandle] 收到房间列表，总数={message.TotalCount}。");
            OnRoomListReceived?.Invoke(message.Rooms);
        }

        private void OnS2C_RoomInfoResult(S2C_RoomInfoResult message)
        {
            if (message == null) return;
            Debug.Log($"[ClientRoomDispatcherHandle] 收到房间信息，RoomId={message.RoomId}, Success={message.Success}。");
            OnRoomInfoReceived?.Invoke(message);
        }

        // [新增] 处理被踢通知
        private void OnS2C_KickedFromRoom(S2C_KickedFromRoom message)
        {
            if (message == null) return;

            Debug.LogWarning($"[ClientRoomDispatcherHandle] 被踢出房间 {message.RoomId}，执行者：{message.ByOwnerSessionId}");

            // 1. 清理本地 Session 上下文
            _sessionContext.ClearCurrentRoomId();
            _model.ClearRoomState();

            // 2. 触发离房事件，驱动 ClientInfrastructure 切换回 InLobby
            OnLeaveRoomSucceeded?.Invoke();
        }

        // [新增] 处理房间解散通知
        private void OnS2C_RoomDismissed(S2C_RoomDismissed message)
        {
            if (message == null) return;

            Debug.LogWarning($"[ClientRoomDispatcherHandle] 房间已解散，原因：{message.Reason}");

            // 1. 清理本地 Session 上下文
            _sessionContext.ClearCurrentRoomId();
            _model.ClearRoomState();

            // 2. 触发离房事件
            OnLeaveRoomSucceeded?.Invoke();
        }
    }
}