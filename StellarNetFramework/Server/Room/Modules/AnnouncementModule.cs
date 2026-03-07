// Assets/StellarNetFramework/Server/Modules/AnnouncementModule.cs

using UnityEngine;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Sender;

namespace StellarNet.Server.Modules
{
    // 公告模块，负责服务端主动向全体在线客户端推送系统公告。
    // 公告内容由业务层提供，框架只负责触发广播发送链。
    // 不接收客户端上行协议，不注册任何 Handler。
    // 公告推送时机由业务层决定（如运营后台触发），不由框架自动触发。
    public sealed class AnnouncementModule : IGlobalService
    {
        private readonly ServerGlobalMessageSender _globalSender;

        // 公告广播委托，由业务层注入，决定公告协议内容与广播范围
        private System.Action<Shared.Protocol.Base.S2CGlobalMessage> _announcementBroadcaster;

        public AnnouncementModule(ServerGlobalMessageSender globalSender)
        {
            if (globalSender == null)
            {
                Debug.LogError("[AnnouncementModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            _globalSender = globalSender;
        }

        // 注入公告广播委托
        public void SetAnnouncementBroadcaster(
            System.Action<Shared.Protocol.Base.S2CGlobalMessage> broadcaster)
        {
            if (broadcaster == null)
            {
                Debug.LogError("[AnnouncementModule] SetAnnouncementBroadcaster 失败：broadcaster 不得为 null");
                return;
            }

            _announcementBroadcaster = broadcaster;
        }

        // 触发公告广播，由业务层主动调用
        // 参数 announcement：待广播的公告协议消息，不得为 null
        public void BroadcastAnnouncement(Shared.Protocol.Base.S2CGlobalMessage announcement)
        {
            if (announcement == null)
            {
                Debug.LogError("[AnnouncementModule] BroadcastAnnouncement 失败：announcement 不得为 null");
                return;
            }

            if (_announcementBroadcaster == null)
            {
                Debug.LogWarning(
                    "[AnnouncementModule] BroadcastAnnouncement 警告：公告广播委托未注入，本次广播已忽略。");
                return;
            }

            _announcementBroadcaster.Invoke(announcement);
        }
    }
}
