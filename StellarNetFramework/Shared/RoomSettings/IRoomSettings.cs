// Assets/StellarNetFramework/Shared/RoomSettings/IRoomSettings.cs

namespace StellarNet.Shared.RoomSettings
{
    // 房间配置快照的双端共同契约接口，定义放在 Shared 层。
    // 不等价于服务端进程配置文件模型，也不属于 NetConfig.json 这类服务端运行配置体系。
    // RoomInstance 依赖的是"实现了 IRoomSettings 的组件"，框架只依赖接口，不依赖具体实现。
    // 具体实现类必须按端归属放在 Server 或 Client，不得放在 Shared 层。
    public interface IRoomSettings
    {
        // 当前房间配置快照格式标识，供框架写入回放文件头
        string SettingsFormat { get; }

        // 当前房间配置快照版本号，供框架写入回放文件头与版本校验
        int SettingsVersion { get; }

        // 返回业务配置快照字节内容。
        // 框架不解析返回内容，仅负责写入、读取与透传。
        // 客户端在回放模式下通过此内容还原本地房间配置。
        byte[] Serialize();
    }
}