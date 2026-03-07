// Assets/StellarNetFramework/Server/Room/Component/IServerRoomComponent.cs

using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Room.RoomScope;
using StellarNet.Server.Room.EventBus;

namespace StellarNet.Server.Room.Component
{
    // 服务端房间业务组件能力声明接口。
    // 所有挂载到 RoomInstance 上的业务组件必须实现此接口。
    // 框架通过此接口统一驱动组件的初始化、Tick 与销毁，不感知具体业务实现。
    // 组件不得在 Init/Tick/OnDestroy 以外的时机主动访问 RoomInstance 内部状态。
    // Handler 注册必须在 Init 内部通过传入的 router 参数完成，组件运行期不得持有 router 引用。
    // 组件 ID（ComponentId）必须在同一 RoomInstance 内唯一，由 ServerRoomAssembler 在装配时校验。
    // 框架层对象（sender、roomInstance）由 ServerRoomAssembler 在装配阶段直接注入，
    // 组件不允许通过任何寻址机制自行获取框架层对象。
    public interface IServerRoomComponent
    {
        // 组件在同一 RoomInstance 内的唯一稳定标识，用于 Router 注销来源校验与日志定位。
        // 必须是编译期确定的常量字符串，不得使用运行时生成的动态字符串。
        string ComponentId { get; }

        // 组件初始化入口，由 ServerRoomAssembler 在装配阶段按顺序调用。
        // 参数 router        — 当前房间的消息路由器，组件在此注册 C2S Handler，Init 完成后不得持有。
        // 参数 serviceLocator — 当前房间的服务定位器，用于业务组件间横向寻址。
        // 参数 eventBus      — 当前房间的领域事件总线，用于组件间领域事件传播。
        // 参数 sender        — 房间域消息发送器，由框架直接注入，组件持有引用用于发送 S2C 消息。
        // 参数 roomInstance  — 当前房间运行时实例，由框架直接注入，组件通过此获取在线成员连接集合。
        // 参数 roomId        — 当前房间 ID，用于日志定位与业务上下文绑定。
        // 返回 true 表示初始化成功；返回 false 表示初始化失败，触发 ServerRoomAssembler 原子回滚。
        bool Init(
            ServerRoomMessageRouter router,
            RoomServiceLocator serviceLocator,
            RoomEventBus eventBus,
            ServerRoomMessageSender sender,
            RoomInstance roomInstance,
            string roomId);

        // 每帧驱动入口，由 RoomInstance.Tick() 按组件注册顺序依次调用。
        // 参数 deltaTimeMs：本帧经过的时间（毫秒），由 GlobalRoomManager.Tick() 统一传入。
        void Tick(long deltaTimeMs);

        // 组件销毁入口，由 RoomInstance 销毁流程按逆序调用。
        // 组件必须在此方法内完成自身资源释放、事件取消订阅与服务注销。
        // 禁止在 OnDestroy 内部发起任何新的网络发送或领域事件。
        void OnDestroy();
    }
}