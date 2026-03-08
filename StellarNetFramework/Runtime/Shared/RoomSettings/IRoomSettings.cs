namespace StellarNet.Shared.RoomSettings
{
    /// <summary>
    /// 房间配置快照的双端共同契约接口，定义放在 Shared 层。
    /// 不等价于服务端进程配置文件模型（NetConfig），也不属于 NetConfig.json 配置体系。
    /// 服务端职责：作为权威房间配置承载对象，参与房间初始化，提供录像写入所需元数据。
    /// 客户端职责：作为房间基础配置的本地只读载体，供表现层读取，回放模式下用于还原本地房间配置。
    /// 具体实现类必须按端归属放在 Server 或 Client 层，不允许在 Shared 层存在具体实现。
    /// 建房时 RoomDispatcher 必须先构建具体 IRoomSettings，再传入 GlobalRoomManager.CreateRoom()。
    /// </summary>
    public interface IRoomSettings
    {
        /// <summary>
        /// 房间配置快照格式标识，用于区分不同版本或不同类型的配置快照结构。
        /// 框架直接读取此字段写入回放文件头，不解析具体内容。
        /// </summary>
        string SettingsFormat { get; }

        /// <summary>
        /// 房间配置快照版本号，用于回放加载时的版本兼容性校验。
        /// 框架直接读取此字段写入回放文件头，不解析具体内容。
        /// </summary>
        int SettingsVersion { get; }

        /// <summary>
        /// 将当前房间配置快照序列化为字节数组。
        /// 框架只负责写入、读取与透传此字节内容，不解析业务字节内容。
        /// </summary>
        byte[] Serialize();
    }
}