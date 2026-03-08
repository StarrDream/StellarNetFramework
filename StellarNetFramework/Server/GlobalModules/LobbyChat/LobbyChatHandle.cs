using StellarNet.Server.Network;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.LobbyChat
{
    /// <summary>
    /// 大厅全局聊天模块 Handle，处理大厅聊天消息收发、历史消息拉取与频率限制。
    /// 非法内容拦截通过虚方法 ValidateChatContent 开放给开发者重写，框架默认直接通过。
    /// 频率限制通过 LobbyChatModel 维护，超出频率时单播 S2C_LobbyChatBlocked 给发送方。
    /// 系统消息由服务端业务层主动调用 BroadcastSystemMessage()，不由客户端请求触发。
    /// </summary>
    public class LobbyChatHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly LobbyChatModel _model;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly GlobalMessageRegistrar _registrar;

        public LobbyChatHandle(
            SessionManager sessionManager,
            LobbyChatModel model,
            ServerGlobalMessageSender globalSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[LobbyChatHandle] 构造失败：sessionManager 为 null。");
                return;
            }
            if (model == null)
            {
                Debug.LogError("[LobbyChatHandle] 构造失败：model 为 null。");
                return;
            }
            if (globalSender == null)
            {
                Debug.LogError("[LobbyChatHandle] 构造失败：globalSender 为 null。");
                return;
            }
            if (registrar == null)
            {
                Debug.LogError("[LobbyChatHandle] 构造失败：registrar 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _model = model;
            _globalSender = globalSender;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<C2S_SendLobbyChat>(OnC2S_SendLobbyChat)
                .Register<C2S_GetLobbyChatHistory>(OnC2S_GetLobbyChatHistory);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<C2S_SendLobbyChat>()
                .Unregister<C2S_GetLobbyChatHistory>();
        }

        private void OnC2S_SendLobbyChat(ConnectionId connectionId, C2S_SendLobbyChat message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[LobbyChatHandle] 大厅聊天失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.Content))
            {
                Debug.LogError($"[LobbyChatHandle] 大厅聊天失败：消息内容为空，SessionId={session.SessionId}。");
                return;
            }

            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 频率限制检查
            if (!_model.CheckRateLimit(session.SessionId, nowMs))
            {
                long cooldownEndMs = _model.GetCooldownEndMs(session.SessionId);
                var blocked = new S2C_LobbyChatBlocked
                {
                    Reason = $"发言过于频繁，请稍后再试"
                };
                _globalSender.SendToSession(session.SessionId, blocked);
                Debug.LogWarning($"[LobbyChatHandle] 大厅聊天频率限制，SessionId={session.SessionId}，CooldownEndMs={cooldownEndMs}。");
                return;
            }

            // 非法内容拦截，开发者可重写 ValidateChatContent 实现具体拦截逻辑
            string blockReason = ValidateChatContent(session.SessionId, message.Content);
            if (!string.IsNullOrEmpty(blockReason))
            {
                var blocked = new S2C_LobbyChatBlocked { Reason = blockReason };
                _globalSender.SendToSession(session.SessionId, blocked);
                Debug.LogWarning($"[LobbyChatHandle] 大厅聊天内容被拦截，SessionId={session.SessionId}，原因={blockReason}。");
                return;
            }

            // 更新发言时间
            _model.UpdateSendTime(session.SessionId, nowMs);

            // 写入历史消息
            var historyItem = new LobbyChatHistoryItem
            {
                SenderSessionId = session.SessionId,
                Content = message.Content,
                SendUnixMs = nowMs,
                MessageType = 0
            };
            _model.AppendHistory(historyItem);

            // 广播给所有在线客户端
            var chatMsg = new S2C_LobbyChatMessage
            {
                SenderSessionId = session.SessionId,
                Content = message.Content,
                SendUnixMs = nowMs,
                MessageType = 0
            };
            _globalSender.BroadcastToAll(chatMsg);
        }

        private void OnC2S_GetLobbyChatHistory(ConnectionId connectionId, C2S_GetLobbyChatHistory message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[LobbyChatHandle] 获取大厅聊天历史失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            int count = message.Count > 0 ? message.Count : 20;
            var history = _model.GetHistory(count);

            var result = new S2C_LobbyChatHistoryResult
            {
                Messages = history.ToArray()
            };

            _globalSender.SendToSession(session.SessionId, result);
        }

        /// <summary>
        /// 服务端业务层主动广播系统消息，不由客户端请求触发。
        /// 系统消息 MessageType=1，不受频率限制与内容拦截约束。
        /// </summary>
        public void BroadcastSystemMessage(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                Debug.LogError("[LobbyChatHandle] BroadcastSystemMessage 失败：content 为空。");
                return;
            }

            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var historyItem = new LobbyChatHistoryItem
            {
                SenderSessionId = "SYSTEM",
                Content = content,
                SendUnixMs = nowMs,
                MessageType = 1
            };
            _model.AppendHistory(historyItem);

            var chatMsg = new S2C_LobbyChatMessage
            {
                SenderSessionId = "SYSTEM",
                Content = content,
                SendUnixMs = nowMs,
                MessageType = 1
            };
            _globalSender.BroadcastToAll(chatMsg);

            Debug.Log($"[LobbyChatHandle] 系统消息已广播，Content={content}。");
        }

        /// <summary>
        /// 非法内容拦截，开发者可重写此方法实现具体拦截逻辑。
        /// 返回非空字符串表示拦截，字符串内容为拦截原因。
        /// 返回 null 或空字符串表示通过。
        /// 框架默认实现直接通过（开发模式），生产环境必须重写。
        /// </summary>
        protected virtual string ValidateChatContent(string sessionId, string content)
        {
            return null;
        }
    }
}
