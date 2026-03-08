// ════════════════════════════════════════════════════════════════
// 文件：TemplateData.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/TemplateData.cs
// 职责：脚手架工具所有配置数据结构的定义层，不承载任何生成逻辑。
//       所有 Generator 与 EditorWindow 通过此层交换数据，保证数据与逻辑解耦。
// ════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace StellarNet.Editor.Scaffold
{
    // ── 生成端枚举 ────────────────────────────────────────────────
    /// <summary>
    /// 代码生成目标端，控制生成器输出哪些端的文件。
    /// </summary>
    public enum GenerateTarget
    {
        BothSides,
        ClientOnly,
        ServerOnly
    }

    // ── 协议方向枚举 ──────────────────────────────────────────────
    /// <summary>
    /// 协议方向，与框架 MessageDirection 对应，用于生成器推导基类。
    /// </summary>
    public enum ProtoDirection
    {
        C2S_Global,
        S2C_Global,
        C2S_Room,
        S2C_Room
    }

    // ── 单条协议定义 ──────────────────────────────────────────────
    /// <summary>
    /// 单条协议的配置数据，由 EditorWindow 填写后传入生成器。
    /// MessageId 必须在生成前完成唯一性校验，生成器不负责校验。
    /// </summary>
    [Serializable]
    public sealed class ProtoDefinition
    {
        /// <summary>协议唯一 ID，框架保留 0-9999，业务从 10000 起。</summary>
        public int MessageId;

        /// <summary>协议类名，例如 C2S_PlayerAction。</summary>
        public string ClassName;

        /// <summary>协议方向，决定继承哪个基类。</summary>
        public ProtoDirection Direction;

        /// <summary>协议字段列表，每项格式为 "类型 字段名"，例如 "string RoomId"。</summary>
        public List<string> Fields = new List<string>();

        /// <summary>协议注释，写入生成文件的 summary 标签。</summary>
        public string Comment;

        public ProtoDefinition()
        {
        }

        public ProtoDefinition(int id, string className, ProtoDirection direction, string comment = "")
        {
            MessageId = id;
            ClassName = className;
            Direction = direction;
            Comment = comment;
        }
    }

    // ── 全局模块配置 ──────────────────────────────────────────────
    /// <summary>
    /// 全局模块生成配置，对应 EditorWindow 全局模块面板的所有输入项。
    /// </summary>
    [Serializable]
    public sealed class GlobalModuleConfig
    {
        // ── 基础信息 ──
        /// <summary>模块名称，例如 Leaderboard。生成的类名前缀来自此字段。</summary>
        public string ModuleName = "MyFeature";

        /// <summary>客户端命名空间。</summary>
        public string ClientNamespace = "Game.Client.GlobalModules";

        /// <summary>服务端命名空间。</summary>
        public string ServerNamespace = "Game.Server.GlobalModules";

        /// <summary>协议命名空间。</summary>
        public string ProtoNamespace = "Game.Shared.Protocol";

        /// <summary>生成端。</summary>
        public GenerateTarget Target = GenerateTarget.BothSides;

        // ── 生成选项 ──
        public bool GenClientModel = true;
        public bool GenClientHandle = true;
        public bool GenServerModel = true;
        public bool GenServerHandle = true;
        public bool GenProtoFile = true;
        public bool GenRegisterStub = true;
        public bool GenServiceLocatorStub = false;
        public bool InjectToInfrastructure = false;

        // ── 协议配置 ──
        /// <summary>协议起始 ID，生成器按此值顺序分配。</summary>
        public int StartMessageId = 10000;

        /// <summary>协议定义列表。</summary>
        public List<ProtoDefinition> Protocols = new List<ProtoDefinition>();

        // ── 输出路径（相对 Assets/）──
        public string ClientOutputPath = "Game/Client/GlobalModules";
        public string ServerOutputPath = "Game/Server/GlobalModules";
        public string ProtoOutputPath = "Game/Shared/Protocol/Global";

        /// <summary>
        /// 返回客户端 Handle 类名。
        /// </summary>
        public string ClientHandleClassName => $"Client{ModuleName}Handle";

        /// <summary>
        /// 返回客户端 Model 类名。
        /// </summary>
        public string ClientModelClassName => $"Client{ModuleName}Model";

        /// <summary>
        /// 返回服务端 Handle 类名。
        /// </summary>
        public string ServerHandleClassName => $"{ModuleName}Handle";

        /// <summary>
        /// 返回服务端 Model 类名。
        /// </summary>
        public string ServerModelClassName => $"{ModuleName}Model";

        /// <summary>
        /// 返回协议文件名（不含扩展名）。
        /// </summary>
        public string ProtoFileName => $"{ModuleName}Messages";
    }

    // ── 房间组件配置 ──────────────────────────────────────────────
    /// <summary>
    /// 房间业务组件生成配置，对应 EditorWindow 房间组件面板的所有输入项。
    /// </summary>
    [Serializable]
    public sealed class RoomComponentConfig
    {
        // ── 基础信息 ──
        /// <summary>组件名称，例如 TurnSystem。</summary>
        public string ComponentName = "MyComponent";

        /// <summary>
        /// 稳定组件注册标识，必须全局唯一且跨版本稳定。
        /// 格式建议：room.模块名（小写下划线），例如 room.turn_system。
        /// </summary>
        public string StableComponentId = "room.my_component";

        /// <summary>服务端命名空间。</summary>
        public string ServerNamespace = "Game.Server.Room.Components";

        /// <summary>客户端命名空间。</summary>
        public string ClientNamespace = "Game.Client.Room.Components";

        /// <summary>协议命名空间。</summary>
        public string ProtoNamespace = "Game.Shared.Protocol";

        /// <summary>生成端。</summary>
        public GenerateTarget Target = GenerateTarget.BothSides;

        // ── 生成选项 ──
        public bool GenServerHandle = true;
        public bool GenServerModel = true;
        public bool GenClientHandle = true;
        public bool GenClientModel = true;
        public bool GenHandlerBindingsStub = true;
        public bool GenLifecycleCallbackStubs = true;
        public bool GenIRoomServiceInterface = false;
        public bool GenServiceLocatorStub = false;
        public bool GenEventBusStub = false;
        public bool GenProtoFile = true;
        public bool GenReconnectSnapshotStub = false;
        public bool InjectToRegistry = false;

        // ── 协议配置 ──
        public int StartMessageId = 11000;
        public List<ProtoDefinition> Protocols = new List<ProtoDefinition>();

        // ── 输出路径 ──
        public string ServerOutputPath = "Game/Server/Room/Components";
        public string ClientOutputPath = "Game/Client/Room/Components";
        public string ProtoOutputPath = "Game/Shared/Protocol/Room";

        // ── 派生类名 ──
        public string ServerHandleClassName => $"Server{ComponentName}Handle";
        public string ServerModelClassName => $"Server{ComponentName}Model";
        public string ClientHandleClassName => $"Client{ComponentName}Handle";
        public string ClientModelClassName => $"Client{ComponentName}Model";
        public string ProtoFileName => $"{ComponentName}Messages";
    }

    // ── 批量生成队列项 ────────────────────────────────────────────
    /// <summary>
    /// 批量生成队列中的单个任务项。
    /// </summary>
    [Serializable]
    public sealed class BatchQueueItem
    {
        public enum ItemType
        {
            GlobalModule,
            RoomComponent,
            ProtoOnly
        }

        public ItemType Type;
        public string DisplayName;
        public GlobalModuleConfig GlobalConfig;
        public RoomComponentConfig RoomConfig;

        /// <summary>当前任务的生成状态。</summary>
        public enum GenerateStatus
        {
            Pending,
            Generating,
            Done,
            Failed
        }

        public GenerateStatus Status = GenerateStatus.Pending;
        public string ErrorMessage;
    }

    // ── 生成结果 ──────────────────────────────────────────────────
    /// <summary>
    /// 单次生成操作的结果，包含所有已写入文件的路径与错误信息。
    /// </summary>
    public sealed class GenerateResult
    {
        public bool Success;
        public List<string> WrittenFiles = new List<string>();
        public List<string> ModifiedFiles = new List<string>();
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public void AddError(string msg) => Errors.Add(msg);
        public void AddWarning(string msg) => Warnings.Add(msg);
        public void AddWritten(string path) => WrittenFiles.Add(path);
        public void AddModified(string path) => ModifiedFiles.Add(path);
    }
}