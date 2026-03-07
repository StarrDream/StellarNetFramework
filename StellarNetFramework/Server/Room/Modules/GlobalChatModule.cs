// Assets/StellarNetFramework/Server/Modules/GlobalChatModule.cs

using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;

namespace StellarNet.Server.Modules
{
    // 全局聊天模块，负责全服聊天消息的接收与广播。
    // 框架只提供协议接入骨架，聊天内容过滤、敏感词检测、频率限制由业务层委托实现。
    // 聊天历史持久化不属于框架职责，由业务层自行维护。
    public sealed class GlobalChatModule : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly ServerGlobalMessageSender _globalSender;

        // 聊天消息处理委托，由业务层注入
        // 返回 true 表示消息通过校验可以广播，false 表示消息被过滤
        private System.Func<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage, bool> _chatMessageValidator;

        // 聊天广播委托，由业务层注入，决定广播目标范围与协议内容
        private System.Action<ConnectionId, SessionId, Shared.Protocol.Base.C2SGlobalMessage> _chatBroadcaster;

        public GlobalChatModule(
            SessionManager sessionManager,
            ServerGlobalMessageRouter globalRouter,
            ServerGlobalMessageSender globalSender)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[GlobalChatModule] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[GlobalChatModule] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[GlobalChatModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            _sessionManager = sessionManager;
            _globalRouter = globalRouter;
            _globalSender = globalSender;
        }

        // 注册聊天协议 Handler
        public void RegisterHandler(System.Type chatMsgType)
        {
            if (chatMsgType == null)
            {
                Debug.LogError("[GlobalChatModule] RegisterHandler 失败：chatMsgType 不得为 null");
                return;
            }

            _globalRouter.Register(chatMsgType, OnChatMessageReceived);
        }

        // 注销聊天协议 Handler
        public void UnregisterHandler(System.Type chatMsgType)
        {
            if (chatMsgType == null)
                return;

            _globalRouter.Unregister(chatMsgType);
        }

        // 注入聊天消息校验委托
        public void SetChatMessageValidator(
            System.Func<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage, bool> validator)
        {
            if (validator == null)
            {
                Debug.LogError("[GlobalChatModule] SetChatMessageValidator 失败：validator 不得为 null");
                return;
            }

            _chatMessageValidator = validator;
        }

        // 注入聊天广播委托
        public void SetChatBroadcaster(
            System.Action<ConnectionId, SessionId, Shared.Protocol.Base.C2SGlobalMessage> broadcaster)
        {
            if (broadcaster == null)
            {
                Debug.LogError("[GlobalChatModule] SetChatBroadcaster 失败：broadcaster 不得为 null");
                return;
            }

            _chatBroadcaster = broadcaster;
        }

        private void OnChatMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[GlobalChatModule] 聊天消息失败：ConnectionId={connectionId} 未绑定有效会话");
                return;
            }

            // 执行业务层校验（频率限制、敏感词等）
            if (_chatMessageValidator != null)
            {
                var passed = _chatMessageValidator.Invoke(connectionId, message);
                if (!passed)
                    return;
            }

            if (_chatBroadcaster == null)
            {
                Debug.LogWarning(
                    $"[GlobalChatModule] 聊天广播委托未注入，SessionId={session.SessionId}，已忽略。");
                return;
            }

            _chatBroadcaster.Invoke(connectionId, session.SessionId, message);
        }
    }
}
