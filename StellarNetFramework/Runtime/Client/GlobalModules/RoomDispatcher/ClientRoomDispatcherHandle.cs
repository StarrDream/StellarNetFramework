// ════════════════════════════════════════════════════════════════
// 文件：ClientRoomDispatcherHandle.cs
// 路径：Assets/StellarNetFramework/Runtime/Client/GlobalModules/RoomDispatcher/ClientRoomDispatcherHandle.cs
// 职责：客户端房间调度模块 Handle。
//       修正：OnCreateRoomSucceeded 事件增加组件清单参数，
//       将 S2C_CreateRoomResult 中的 RoomComponentIds 抛出给 Infrastructure。
// ════════════════════════════════════════════════════════════════

using StellarNet.Client.Network;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 客户端房间调度模块 Handle，处理建房、加房结果。
    /// 修正：建房成功事件增加组件列表参数，支持动态装配。
    /// </summary>
    public sealed class ClientRoomDispatcherHandle
    {
        private readonly ClientRoomDispatcherModel _model;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRegistrar _registrar;

        // [修改] 事件签名增加 string[] components 参数，与加房成功保持一致
        public event System.Action<string, string[]> OnCreateRoomSucceeded;
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
                .Register<S2C_JoinRoomResult>(OnS2C_JoinRoomResult)
                .Register<S2C_LeaveRoomResult>(OnS2C_LeaveRoomResult);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_CreateRoomResult>()
                .Unregister<S2C_JoinRoomResult>()
                .Unregister<S2C_LeaveRoomResult>();
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

            // [修改] 传递服务端下发的组件列表
            // 客户端不应该揣测房间里有什么组件，完全信赖服务端下发的数据
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

            // 只要服务端返回结果，无论是否 Success（通常都是 True），都视为本地离房成功
            // 清理本地 Session 上下文
            _sessionContext.ClearCurrentRoomId();
            _model.ClearRoomState();
            // 触发事件，驱动 ClientInfrastructure 切换回 InLobby 状态
            OnLeaveRoomSucceeded?.Invoke();
            Debug.Log("[ClientRoomDispatcherHandle] 离房成功，已触发状态回调。");
        }
    }
}