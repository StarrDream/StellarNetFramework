using StellarNet.Client.Network;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 客户端房间调度模块 Handle，处理建房、加房结果。
    /// 修复：移除了属于房间域的成员变动协议监听，严格遵守全局域/房间域边界。
    /// </summary>
    public sealed class ClientRoomDispatcherHandle
    {
        private readonly ClientRoomDispatcherModel _model;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRegistrar _registrar;

        public event System.Action<string> OnCreateRoomSucceeded;
        public event System.Action<string> OnCreateRoomFailed;
        public event System.Action<string, string[]> OnJoinRoomSucceeded;
        public event System.Action<string> OnJoinRoomFailed;
        public event System.Action OnLeaveRoomSucceeded;

        public ClientRoomDispatcherHandle(
            ClientRoomDispatcherModel model,
            ClientSessionContext sessionContext,
            ClientGlobalMessageRegistrar registrar)
        {
            if (model == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] 构造失败：model 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] 构造失败：sessionContext 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] 构造失败：registrar 为 null。");
                return;
            }

            _model = model;
            _sessionContext = sessionContext;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<S2C_CreateRoomResult>(OnS2C_CreateRoomResult)
                .Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_CreateRoomResult>()
                .Unregister<S2C_JoinRoomResult>();
        }

        private void OnS2C_CreateRoomResult(S2C_CreateRoomResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] OnS2C_CreateRoomResult 失败：message 为 null。");
                return;
            }

            if (!message.Success)
            {
                _model.SetCreateFailed(message.FailReason);
                OnCreateRoomFailed?.Invoke(message.FailReason);
                Debug.Log($"[ClientRoomDispatcherHandle] 建房失败，原因={message.FailReason}。");
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                Debug.LogError("[ClientRoomDispatcherHandle] 建房结果异常：Success=true 但 RoomId 为空，已忽略。");
                _model.SetCreateFailed("服务端返回 RoomId 为空");
                OnCreateRoomFailed?.Invoke("服务端返回 RoomId 为空");
                return;
            }

            _sessionContext.SetCurrentRoomId(message.RoomId);
            _model.SetCreateSucceeded();
            OnCreateRoomSucceeded?.Invoke(message.RoomId);

            Debug.Log($"[ClientRoomDispatcherHandle] 建房成功，RoomId={message.RoomId}。");
        }

        private void OnS2C_JoinRoomResult(S2C_JoinRoomResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] OnS2C_JoinRoomResult 失败：message 为 null。");
                return;
            }

            if (!message.Success)
            {
                _model.SetJoinFailed(message.FailReason);
                OnJoinRoomFailed?.Invoke(message.FailReason);
                Debug.Log($"[ClientRoomDispatcherHandle] 加房失败，原因={message.FailReason}。");
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                Debug.LogError("[ClientRoomDispatcherHandle] 加房结果异常：Success=true 但 RoomId 为空，已忽略。");
                _model.SetJoinFailed("服务端返回 RoomId 为空");
                OnJoinRoomFailed?.Invoke("服务端返回 RoomId 为空");
                return;
            }

            _sessionContext.SetCurrentRoomId(message.RoomId);
            _model.SetJoinSucceeded(message.RoomId, message.RoomComponentIds);
            OnJoinRoomSucceeded?.Invoke(message.RoomId, message.RoomComponentIds);

            Debug.Log(
                $"[ClientRoomDispatcherHandle] 加房成功，RoomId={message.RoomId}，组件数量={message.RoomComponentIds?.Length ?? 0}。");
        }
    }
}