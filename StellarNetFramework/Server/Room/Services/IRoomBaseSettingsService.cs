using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;

namespace StellarNet.Server.Room.Services
{
    /// <summary>
    /// 房间基础设置服务接口。
    /// 这是房间基础骨架组件对外暴露的稳定服务契约，供：
    /// 1. 同房间其他业务组件通过 RoomScopeServiceLocator 按接口访问
    /// 2. GlobalRoomManager 作为跨域代理访问
    /// 外部禁止直接依赖具体组件实现类，防止跨层耦合扩散。
    /// </summary>
    public interface IRoomBaseSettingsService : IRoomService
    {
        /// <summary>
        /// 通知基础设置组件有成员加入。
        /// 组件负责完成成员骨架同步、快照单播、成员广播与事件发布。
        /// </summary>
        void NotifyMemberJoined(string sessionId);

        /// <summary>
        /// 通知基础设置组件有成员离开。
        /// 组件负责完成成员骨架同步、房主重选、成员快照广播与事件发布。
        /// </summary>
        void NotifyMemberLeft(string sessionId, string reason);

        /// <summary>
        /// 通知基础设置组件某成员完成重连接管。
        /// 组件负责向该成员补发基础快照与成员快照。
        /// </summary>
        void NotifyReconnectRecovered(string sessionId);

        /// <summary>
        /// 获取当前成员快照列表。
        /// 返回值必须是快照副本，调用方不得持有内部集合引用。
        /// </summary>
        List<RoomMemberSnapshot> GetMemberSnapshots();

        /// <summary>
        /// 获取当前房主 SessionId。
        /// </summary>
        string GetOwnerSessionId();

        /// <summary>
        /// 获取当前房间是否满足开始条件。
        /// </summary>
        bool GetCanStart();

        /// <summary>
        /// 构建当前房间基础状态快照。
        /// 供新加入成员或重连成员做基础状态恢复。
        /// </summary>
        S2C_RoomBaseSettingsSnapshot BuildBaseSnapshot();

        /// <summary>
        /// 清空服务内部运行时状态。
        /// 由房间销毁或组件反初始化阶段调用。
        /// </summary>
        void ClearState();
    }
}