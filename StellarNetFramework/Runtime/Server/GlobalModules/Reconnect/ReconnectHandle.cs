using StellarNet.Server.Network;
using StellarNet.Server.Room;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.Reconnect
{
    public sealed class ReconnectHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _roomManager;
        private readonly ReconnectModel _model;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly GlobalMessageRegistrar _registrar;

        public ReconnectHandle(
            SessionManager sessionManager,
            GlobalRoomManager roomManager,
            ReconnectModel model,
            ServerGlobalMessageSender globalSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[ReconnectHandle] 构造失败：sessionManager 为 null。");
                return;
            }

            if (roomManager == null)
            {
                Debug.LogError("[ReconnectHandle] 构造失败：roomManager 为 null。");
                return;
            }

            if (model == null)
            {
                Debug.LogError("[ReconnectHandle] 构造失败：model 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[ReconnectHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ReconnectHandle] 构造失败：registrar 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _roomManager = roomManager;
            _model = model;
            _globalSender = globalSender;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar.Register<C2S_Reconnect>(OnC2S_Reconnect);
        }

        public void UnregisterAll()
        {
            _registrar.Unregister<C2S_Reconnect>();
        }

        private void OnC2S_Reconnect(ConnectionId connectionId, C2S_Reconnect message)
        {
            if (string.IsNullOrEmpty(message.SessionId))
            {
                Debug.LogError($"[ReconnectHandle] 重连失败：SessionId 为空，ConnectionId={connectionId}。");
                SendReconnectFail(connectionId, "SessionId 不能为空");
                return;
            }

            var session = _sessionManager.GetSessionById(message.SessionId);
            if (session == null)
            {
                Debug.LogError($"[ReconnectHandle] 重连失败：SessionId={message.SessionId} 不存在或已过期，ConnectionId={connectionId}。");
                SendReconnectFail(connectionId, "会话不存在或已过期，请重新登录");
                return;
            }

            if (_model.IsReconnecting(message.SessionId))
            {
                Debug.LogWarning($"[ReconnectHandle] 重连请求重复触发，SessionId={message.SessionId}，ConnectionId={connectionId}，已忽略。");
                return;
            }

            if (!_model.TryMarkReconnecting(message.SessionId))
            {
                Debug.LogWarning($"[ReconnectHandle] 重连标记失败，SessionId={message.SessionId}，ConnectionId={connectionId}，已忽略。");
                return;
            }

            bool takeoverSuccess = _sessionManager.TakeoverSession(message.SessionId, connectionId);
            if (!takeoverSuccess)
            {
                _model.ClearReconnecting(message.SessionId);
                Debug.LogError($"[ReconnectHandle] 重连失败：会话接管失败，SessionId={message.SessionId}，ConnectionId={connectionId}。");
                SendReconnectFail(connectionId, "会话接管失败，请重新登录");
                return;
            }

            string originalRoomId = session.CurrentRoomId;
            string targetRoomId = originalRoomId;
            string[] roomComponentIds = new string[0];

            if (!string.IsNullOrEmpty(targetRoomId))
            {
                var room = _roomManager.GetRoom(targetRoomId);
                if (room != null)
                {
                    room.UpdateMemberConnection(message.SessionId, connectionId);

                    // [修复] 移除此处立即下发房间快照的逻辑
                    // 改为等待客户端装配完成后发送 C2S_ReconnectRoomReady，再由 ServerRoomBaseSettingsHandle 下发

                    roomComponentIds = room.GetComponentIds();
                }
                else
                {
                    session.UnbindRoom();
                    targetRoomId = string.Empty;
                    Debug.LogWarning($"[ReconnectHandle] 重连时目标房间已不存在，SessionId={message.SessionId}，原 RoomId={originalRoomId}，已清空房间绑定。");
                }
            }

            string targetState = string.IsNullOrEmpty(targetRoomId) ? "InLobby" : "InRoom";

            var result = new S2C_ReconnectResult
            {
                Success = true,
                FailReason = string.Empty,
                TargetState = targetState,
                TargetRoomId = targetRoomId ?? string.Empty,
                RoomComponentIds = roomComponentIds ?? new string[0]
            };

            _globalSender.SendToSession(message.SessionId, result);
            _model.ClearReconnecting(message.SessionId);

            Debug.Log($"[ReconnectHandle] 重连成功，SessionId={message.SessionId}，ConnectionId={connectionId}，TargetState={targetState}，TargetRoomId={targetRoomId}。");
        }

        private void SendReconnectFail(ConnectionId connectionId, string reason)
        {
            var tempSession = _sessionManager.CreateSession(connectionId);
            if (tempSession == null)
            {
                Debug.LogError($"[ReconnectHandle] 无法下发重连失败结果：临时 Session 创建失败，ConnectionId={connectionId}。");
                return;
            }

            var result = new S2C_ReconnectResult
            {
                Success = false,
                FailReason = reason,
                TargetState = string.Empty,
                TargetRoomId = string.Empty,
                RoomComponentIds = new string[0]
            };

            _globalSender.SendToSession(tempSession.SessionId, result);
            _sessionManager.DestroySession(tempSession.SessionId);
        }
    }
}