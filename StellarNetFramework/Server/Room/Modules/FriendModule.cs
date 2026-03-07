// Assets/StellarNetFramework/Server/Modules/FriendModule.cs

using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;

namespace StellarNet.Server.Modules
{
    // 好友模块，负责好友邀请、好友列表查询等全局好友业务的协议接入。
    // 框架只提供协议接入骨架与 Handler 注册机制，具体好友数据持久化由业务层实现。
    // 好友业务数据不存储在 SessionData 中，由业务层自行维护数据源。
    public sealed class FriendModule : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly ServerGlobalMessageSender _globalSender;

        // 好友业务处理委托，由业务层注入
        // 参数1：发起方 ConnectionId
        // 参数2：好友相关协议消息体
        private System.Action<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage> _friendRequestHandler;
        private System.Action<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage> _friendListQueryHandler;

        public FriendModule(
            SessionManager sessionManager,
            ServerGlobalMessageRouter globalRouter,
            ServerGlobalMessageSender globalSender)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[FriendModule] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[FriendModule] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[FriendModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            _sessionManager = sessionManager;
            _globalRouter = globalRouter;
            _globalSender = globalSender;
        }

        // 注册好友相关协议 Handler
        public void RegisterHandlers(
            System.Type friendRequestMsgType,
            System.Type friendListQueryMsgType)
        {
            if (friendRequestMsgType != null)
                _globalRouter.Register(friendRequestMsgType, OnFriendRequestReceived);

            if (friendListQueryMsgType != null)
                _globalRouter.Register(friendListQueryMsgType, OnFriendListQueryReceived);
        }

        // 注销好友相关协议 Handler
        public void UnregisterHandlers(
            System.Type friendRequestMsgType,
            System.Type friendListQueryMsgType)
        {
            if (friendRequestMsgType != null)
                _globalRouter.Unregister(friendRequestMsgType);

            if (friendListQueryMsgType != null)
                _globalRouter.Unregister(friendListQueryMsgType);
        }

        // 注入好友请求处理委托
        public void SetFriendRequestHandler(
            System.Action<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage> handler)
        {
            if (handler == null)
            {
                Debug.LogError("[FriendModule] SetFriendRequestHandler 失败：handler 不得为 null");
                return;
            }

            _friendRequestHandler = handler;
        }

        // 注入好友列表查询处理委托
        public void SetFriendListQueryHandler(
            System.Action<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage> handler)
        {
            if (handler == null)
            {
                Debug.LogError("[FriendModule] SetFriendListQueryHandler 失败：handler 不得为 null");
                return;
            }

            _friendListQueryHandler = handler;
        }

        private void OnFriendRequestReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[FriendModule] 好友请求失败：ConnectionId={connectionId} 未绑定有效会话");
                return;
            }

            if (_friendRequestHandler == null)
            {
                Debug.LogWarning(
                    $"[FriendModule] 好友请求处理委托未注入，SessionId={session.SessionId}，已忽略。");
                return;
            }

            _friendRequestHandler.Invoke(connectionId, message);
        }

        private void OnFriendListQueryReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[FriendModule] 好友列表查询失败：ConnectionId={connectionId} 未绑定有效会话");
                return;
            }

            if (_friendListQueryHandler == null)
            {
                Debug.LogWarning(
                    $"[FriendModule] 好友列表查询处理委托未注入，SessionId={session.SessionId}，已忽略。");
                return;
            }

            _friendListQueryHandler.Invoke(connectionId, message);
        }
    }
}
