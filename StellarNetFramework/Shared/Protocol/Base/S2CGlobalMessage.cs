// Assets/StellarNetFramework/Shared/Protocol/Base/S2CGlobalMessage.cs

namespace StellarNet.Shared.Protocol.Base
{
    // 服务端下行、全局域协议基类。
    // 继承此基类的协议类型，其方向与域归属在类型系统层即被制度化固定为：
    // 方向 = S2C，域 = Global。
    // 典型场景：登录结果、重连结果、好友邀请、公告推送、全局系统通知。
    // 不依赖当前房间运行时归属上下文。
    // 服务端入站链若收到此类型协议，必须视为非法协议并立即阻断。
    public abstract class S2CGlobalMessage
    {
    }
}