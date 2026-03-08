using StellarNet.Client.Network;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 客户端房间调度模块 Handle，处理建房、加房结果与当前过渡阶段下的成员变化通知。
    /// 说明：
    ///   S2C_MemberJoined / S2C_MemberLeft 已经归属房间基础设置组件协议，
    ///   这里只是为了兼容当前客户端尚未落地房间基础设置组件 Handle 的过渡接法。
    ///   待客户端房间基础设置组件 Handle 落地后，应将这两个协议监听迁移出去。
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
        public event System.Action<string> OnMemberJoined;
        public event System.Action<string, string> OnMemberLeft;

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
                .Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
                .Register<S2C_MemberJoined>(OnS2C_MemberJoined)
                .Register<S2C_MemberLeft>(OnS2C_MemberLeft);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_CreateRoomResult>()
                .Unregister<S2C_JoinRoomResult>()
                .Unregister<S2C_MemberJoined>()
                .Unregister<S2C_MemberLeft>();
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

        private void OnS2C_MemberJoined(S2C_MemberJoined message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] OnS2C_MemberJoined 失败：message 为 null。");
                return;
            }

            OnMemberJoined?.Invoke(message.SessionId);
        }

        private void OnS2C_MemberLeft(S2C_MemberLeft message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientRoomDispatcherHandle] OnS2C_MemberLeft 失败：message 为 null。");
                return;
            }

            OnMemberLeft?.Invoke(message.SessionId, message.Reason);
        }
    }
}