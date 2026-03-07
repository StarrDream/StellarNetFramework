// Assets/StellarNetFramework/Shared/Protocol/Base/C2SGlobalMessage.cs

namespace StellarNet.Shared.Protocol.Base
{
    // 客户端上行、全局域协议基类。
    // 继承此基类的协议类型，其方向与域归属在类型系统层即被制度化固定为：
    // 方向 = C2S，域 = Global。
    // 典型场景：登录、建房、加房请求、重连认证、好友、公告、全局聊天。
    // 不进入房间归属一致性校验链，不依赖当前房间运行时归属上下文。
    // 框架不得依赖目录名、命名约定或手填配置猜测此基类已表达的方向与域归属。
    public abstract class C2SGlobalMessage
    {
    }
}