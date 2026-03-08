using StellarNet.Server.Network;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.Announcement
{
    /// <summary>
    /// 公告模块 Handle，处理公告列表拉取、已读状态上报与新公告主动推送。
    /// 新公告推送由服务端业务层主动调用 PushAnnouncement()，不由客户端请求触发。
    /// 已读状态以 SessionId 为粒度维护，Session 销毁后已读状态随之失效，不做持久化。
    /// </summary>
    public sealed class AnnouncementHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly AnnouncementModel _model;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly GlobalMessageRegistrar _registrar;

        public AnnouncementHandle(
            SessionManager sessionManager,
            AnnouncementModel model,
            ServerGlobalMessageSender globalSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[AnnouncementHandle] 构造失败：sessionManager 为 null。");
                return;
            }
            if (model == null)
            {
                Debug.LogError("[AnnouncementHandle] 构造失败：model 为 null。");
                return;
            }
            if (globalSender == null)
            {
                Debug.LogError("[AnnouncementHandle] 构造失败：globalSender 为 null。");
                return;
            }
            if (registrar == null)
            {
                Debug.LogError("[AnnouncementHandle] 构造失败：registrar 为 null。");
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
                .Register<C2S_GetAnnouncementList>(OnC2S_GetAnnouncementList)
                .Register<C2S_MarkAnnouncementRead>(OnC2S_MarkAnnouncementRead);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<C2S_GetAnnouncementList>()
                .Unregister<C2S_MarkAnnouncementRead>();
        }

        private void OnC2S_GetAnnouncementList(ConnectionId connectionId, C2S_GetAnnouncementList message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[AnnouncementHandle] 获取公告列表失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            int pageSize = message.PageSize > 0 ? message.PageSize : 10;
            var announcements = _model.GetAnnouncementList(message.PageIndex, pageSize);

            var result = new S2C_AnnouncementListResult
            {
                Announcements = announcements.ToArray(),
                TotalCount = _model.TotalCount
            };

            _globalSender.SendToSession(session.SessionId, result);
        }

        private void OnC2S_MarkAnnouncementRead(ConnectionId connectionId, C2S_MarkAnnouncementRead message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[AnnouncementHandle] 标记公告已读失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.AnnouncementId))
            {
                Debug.LogError($"[AnnouncementHandle] 标记公告已读失败：AnnouncementId 为空，SessionId={session.SessionId}。");
                return;
            }

            _model.MarkRead(session.SessionId, message.AnnouncementId);

            var result = new S2C_MarkAnnouncementReadResult
            {
                Success = true,
                AnnouncementId = message.AnnouncementId
            };

            _globalSender.SendToSession(session.SessionId, result);
        }

        /// <summary>
        /// 主动推送新公告给所有在线客户端，由服务端业务层调用。
        /// 推送前先将公告写入 Model，再广播给全体在线客户端。
        /// </summary>
        public void PushAnnouncement(AnnouncementInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.AnnouncementId))
            {
                Debug.LogError("[AnnouncementHandle] PushAnnouncement 失败：info 为 null 或 AnnouncementId 为空。");
                return;
            }

            _model.AddAnnouncement(info);

            var push = new S2C_AnnouncementPush { Announcement = info };
            _globalSender.BroadcastToAll(push);

            Debug.Log($"[AnnouncementHandle] 公告已推送，AnnouncementId={info.AnnouncementId}，Title={info.Title}。");
        }
    }
}
