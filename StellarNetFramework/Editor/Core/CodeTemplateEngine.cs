// ════════════════════════════════════════════════════════════════
// 文件：CodeTemplateEngine.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/CodeTemplateEngine.cs
// 职责：代码模板引擎，提供所有代码片段的字符串构建能力。
//       只负责字符串拼接与格式化，不负责��件 IO 与路径解析。
//       所有 Generator 通过此引擎获取代码片段，保证生成风格统一。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 代码模板引擎，提供标准化的代码片段构建方法。
    /// 所有方法均为纯函数，无副作用，可安全并发调用。
    /// 缩进统一使用 4 个空格，与框架现有代码风格保持一致。
    /// </summary>
    public static class CodeTemplateEngine
    {
        // ── 文件头 ────────────────────────────────────────────────

        /// <summary>
        /// 生成标准文件头注释块。
        /// 包含自动生成声明、生成时间与框架版本标识。
        /// </summary>
        public static string BuildFileHeader(string fileName, string description)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// ════════════════════════════════════════════════════════════════");
            sb.AppendLine($"// 此文件由 StellarNet 脚手架工具自动生成");
            sb.AppendLine($"// 文件：{fileName}");
            sb.AppendLine($"// 生成时间：{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"// 说明：{description}");
            sb.AppendLine("// 可在此基础上添加业务逻辑，请勿删除自动生成的结构性代码。");
            sb.AppendLine("// ════════════════════════════════════════════════════════════════");
            sb.AppendLine();
            return sb.ToString();
        }

        // ── Using 块 ──────────────────────────────────────────────

        /// <summary>
        /// 生成 using 指令块，自动去重并按字母排序。
        /// </summary>
        public static string BuildUsings(IEnumerable<string> namespaces)
        {
            var sorted = new SortedSet<string>(namespaces);
            var sb = new StringBuilder();
            foreach (var ns in sorted)
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            return sb.ToString();
        }

        // ── 命名空间包装 ──────────────────────────────────────────

        /// <summary>
        /// 将代码体包装在命名空间块中。
        /// </summary>
        public static string WrapInNamespace(string namespaceName, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            // 对 body 每行添加 4 空格缩进
            foreach (var line in body.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(trimmed))
                    sb.AppendLine();
                else
                    sb.AppendLine($"    {trimmed}");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── Summary 注释 ──────────────────────────────────────────

        /// <summary>
        /// 生成标准 XML summary 注释，支持多行。
        /// indent 参数控制缩进层级（每级 4 空格）。
        /// </summary>
        public static string BuildSummary(string comment, int indent = 0)
        {
            string pad = new string(' ', indent * 4);
            var sb = new StringBuilder();
            sb.AppendLine($"{pad}/// <summary>");
            foreach (var line in comment.Split('\n'))
            {
                sb.AppendLine($"{pad}/// {line.Trim()}");
            }

            sb.AppendLine($"{pad}/// </summary>");
            return sb.ToString();
        }

        // ── 协议基类映射 ──────────────────────────────────────────

        /// <summary>
        /// 根据协议方向返回对应的框架基类名。
        /// </summary>
        public static string GetProtoBaseClass(ProtoDirection direction)
        {
            return direction switch
            {
                ProtoDirection.C2S_Global => "C2SGlobalMessage",
                ProtoDirection.S2C_Global => "S2CGlobalMessage",
                ProtoDirection.C2S_Room => "C2SRoomMessage",
                ProtoDirection.S2C_Room => "S2CRoomMessage",
                _ => "C2SGlobalMessage"
            };
        }

        /// <summary>
        /// 根据协议方向返回可读标签，用于注释与日志。
        /// </summary>
        public static string GetProtoDirectionLabel(ProtoDirection direction)
        {
            return direction switch
            {
                ProtoDirection.C2S_Global => "客户端上行全局域",
                ProtoDirection.S2C_Global => "服务端下行全局域",
                ProtoDirection.C2S_Room => "客户端上行房间域",
                ProtoDirection.S2C_Room => "服务端下行房间域",
                _ => "未知"
            };
        }

        // ── 协议类生成 ────────────────────────────────────────────

        /// <summary>
        /// 生成单条协议类定义代码块。
        /// 包含 MessageId 特性、summary 注释、基类继承与字段列表。
        /// </summary>
        public static string BuildProtoClass(ProtoDefinition proto)
        {
            var sb = new StringBuilder();
            string baseClass = GetProtoBaseClass(proto.Direction);
            string dirLabel = GetProtoDirectionLabel(proto.Direction);
            string comment = string.IsNullOrEmpty(proto.Comment)
                ? $"{proto.ClassName}。属于{dirLabel}协议。"
                : proto.Comment;

            sb.Append(BuildSummary(comment, 0));
            sb.AppendLine($"[MessageId({proto.MessageId})]");
            sb.AppendLine($"public sealed class {proto.ClassName} : {baseClass}");
            sb.AppendLine("{");

            if (proto.Fields != null && proto.Fields.Count > 0)
            {
                foreach (var field in proto.Fields)
                {
                    sb.AppendLine($"    public {field};");
                }
            }
            else
            {
                sb.AppendLine("    // TODO: 添加协议字段");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            return sb.ToString();
        }

        // ── 全局模块 Handle（客户端）────────────────────────────────

        /// <summary>
        /// 生成客户端全局模块 Handle 类的完整代码体（不含 namespace 包装）。
        /// 包含构造器防御检查、RegisterAll/UnregisterAll、协议处理方法桩。
        /// </summary>
        public static string BuildClientGlobalHandleBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ClientHandleClassName;
            string modelClass = cfg.ClientModelClassName;

            // 收集 S2C 协议用于注册
            var s2cProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.S2C_Global)
                    s2cProtos.Add(p);
            }

            sb.Append(BuildSummary(
                $"客户端 {cfg.ModuleName} 模块 Handle，处理来自服务端的下行协议。\n" +
                $"由脚手架生成，请在协议处理方法中实现具体业务逻辑。\n" +
                $"Handle 不直接操作 View，通过 Model 状态变更与事件驱动 View 刷新（MSV 架构）。", 0));

            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");

            // 字段
            sb.AppendLine($"    private readonly {modelClass} _model;");
            sb.AppendLine("    private readonly ClientGlobalMessageRegistrar _registrar;");
            sb.AppendLine();

            // 事件（为每个 S2C 协议生成一个对外事件）
            if (s2cProtos.Count > 0)
            {
                sb.AppendLine("    // 供 View 层订阅的业务事件，Handle 不直接操作 View");
                foreach (var p in s2cProtos)
                {
                    string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                    sb.AppendLine($"    public event System.Action<{p.ClassName}> {evtName};");
                }

                sb.AppendLine();
            }

            // 构造器
            sb.AppendLine($"    public {className}(");
            sb.AppendLine($"        {modelClass} model,");
            sb.AppendLine("        ClientGlobalMessageRegistrar registrar)");
            sb.AppendLine("    {");
            sb.AppendLine($"        if (model == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：model 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (registrar == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：registrar 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        _model = model;");
            sb.AppendLine("        _registrar = registrar;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // RegisterAll
            sb.AppendLine("    public void RegisterAll()");
            sb.AppendLine("    {");
            if (s2cProtos.Count > 0)
            {
                sb.Append("        _registrar");
                for (int i = 0; i < s2cProtos.Count; i++)
                {
                    string handlerName = $"On{s2cProtos[i].ClassName}";
                    string suffix = (i < s2cProtos.Count - 1) ? "" : ";";
                    sb.AppendLine();
                    sb.Append($"            .Register<{s2cProtos[i].ClassName}>({handlerName}){suffix}");
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // TODO: 注册协议处理方法");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // UnregisterAll
            sb.AppendLine("    public void UnregisterAll()");
            sb.AppendLine("    {");
            if (s2cProtos.Count > 0)
            {
                sb.Append("        _registrar");
                for (int i = 0; i < s2cProtos.Count; i++)
                {
                    string suffix = (i < s2cProtos.Count - 1) ? "" : ";";
                    sb.AppendLine();
                    sb.Append($"            .Unregister<{s2cProtos[i].ClassName}>(){suffix}");
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // TODO: 注销协议处理方法");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // 协议处理方法桩
            foreach (var p in s2cProtos)
            {
                string handlerName = $"On{p.ClassName}";
                string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                sb.AppendLine($"    private void {handlerName}({p.ClassName} message)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (message == null)");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            UnityEngine.Debug.LogError(\"[{className}] {handlerName} 失败：message 为 null。\");");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine("        // TODO: 更新 Model 状态");
                sb.AppendLine($"        {evtName}?.Invoke(message);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 全局模块 Model（客户端）──────────────────────────────────

        /// <summary>
        /// 生成客户端全局模块 Model 类的完整代码体（不含 namespace 包装）。
        /// </summary>
        public static string BuildClientGlobalModelBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ClientModelClassName;

            sb.Append(BuildSummary(
                $"客户端 {cfg.ModuleName} 模块 Model，维护本地运行时状态。\n" +
                $"不承载业务逻辑，不直接驱动任何网络发送。", 0));

            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 添加状态字段");
            sb.AppendLine();
            sb.AppendLine($"    public {className}()");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 初始化默认状态");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 全局模块 Handle（服务端）────────────────────────────────

        /// <summary>
        /// 生成服务端全局模块 Handle 类的完整代码体（不含 namespace 包装）。
        /// 包含 IGlobalService 标记（可选）、构造器防御检查、RegisterAll/UnregisterAll、协议处理方法桩。
        /// </summary>
        public static string BuildServerGlobalHandleBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ServerHandleClassName;
            string modelClass = cfg.ServerModelClassName;

            // 收集 C2S 协议
            var c2sProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.C2S_Global)
                    c2sProtos.Add(p);
            }

            string implements = cfg.GenServiceLocatorStub ? " : IGlobalService" : "";

            sb.Append(BuildSummary(
                $"服务端 {cfg.ModuleName} 模块 Handle，处理客户端上行协议。\n" +
                $"由脚手架生成，请在协议处理方法中实现具体业务逻辑。", 0));

            sb.AppendLine($"public sealed class {className}{implements}");
            sb.AppendLine("{");

            // 字段
            sb.AppendLine("    private readonly SessionManager _sessionManager;");
            sb.AppendLine($"    private readonly {modelClass} _model;");
            sb.AppendLine("    private readonly ServerGlobalMessageSender _globalSender;");
            sb.AppendLine("    private readonly GlobalMessageRegistrar _registrar;");
            sb.AppendLine();

            // 构造器
            sb.AppendLine($"    public {className}(");
            sb.AppendLine("        SessionManager sessionManager,");
            sb.AppendLine($"        {modelClass} model,");
            sb.AppendLine("        ServerGlobalMessageSender globalSender,");
            sb.AppendLine("        GlobalMessageRegistrar registrar)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (sessionManager == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：sessionManager 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (model == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：model 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (globalSender == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：globalSender 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (registrar == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：registrar 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        _sessionManager = sessionManager;");
            sb.AppendLine("        _model = model;");
            sb.AppendLine("        _globalSender = globalSender;");
            sb.AppendLine("        _registrar = registrar;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // RegisterAll
            sb.AppendLine("    public void RegisterAll()");
            sb.AppendLine("    {");
            if (c2sProtos.Count > 0)
            {
                sb.Append("        _registrar");
                for (int i = 0; i < c2sProtos.Count; i++)
                {
                    string handlerName = $"On{c2sProtos[i].ClassName}";
                    string suffix = (i < c2sProtos.Count - 1) ? "" : ";";
                    sb.AppendLine();
                    sb.Append($"            .Register<{c2sProtos[i].ClassName}>({handlerName}){suffix}");
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // TODO: 注册协议处理方法");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // UnregisterAll
            sb.AppendLine("    public void UnregisterAll()");
            sb.AppendLine("    {");
            if (c2sProtos.Count > 0)
            {
                sb.Append("        _registrar");
                for (int i = 0; i < c2sProtos.Count; i++)
                {
                    string suffix = (i < c2sProtos.Count - 1) ? "" : ";";
                    sb.AppendLine();
                    sb.Append($"            .Unregister<{c2sProtos[i].ClassName}>(){suffix}");
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // TODO: 注销协议处理方法");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // 协议处理方法桩
            foreach (var p in c2sProtos)
            {
                string handlerName = $"On{p.ClassName}";
                sb.AppendLine($"    private void {handlerName}(ConnectionId connectionId, {p.ClassName} message)");
                sb.AppendLine("    {");
                sb.AppendLine("        var session = _sessionManager.GetSessionByConnectionId(connectionId);");
                sb.AppendLine("        if (session == null)");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            UnityEngine.Debug.LogError($\"[{className}] {handlerName} 失败：ConnectionId={{connectionId}} 未绑定有效会话。\");");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine("        // TODO: 实现业务逻辑");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 全局模块 Model（服务端）──────────────────────────────────

        /// <summary>
        /// 生成服务端全局模块 Model 类的完整代码体（不含 namespace 包装）。
        /// </summary>
        public static string BuildServerGlobalModelBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ServerModelClassName;

            sb.Append(BuildSummary(
                $"服务端 {cfg.ModuleName} 模块 Model，维护服务端运行时状态。\n" +
                $"不承载业务逻辑，不直接驱动任何网络发送。", 0));

            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 添加状态字段");
            sb.AppendLine();
            sb.AppendLine($"    public {className}()");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 初始化默认状态");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 房间组件 Handle（服务端）────────────────────────────────

        /// <summary>
        /// 生成服务端房间组件 Handle 类的完整代码体（不含 namespace 包装）。
        /// 实现 IInitializableRoomComponent，包含全部生命周期回调桩与 GetHandlerBindings 桩。
        /// </summary>
        public static string BuildServerRoomHandleBody(RoomComponentConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ServerHandleClassName;
            string modelClass = cfg.ServerModelClassName;

            var c2sProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.C2S_Room)
                    c2sProtos.Add(p);
            }

            sb.Append(BuildSummary(
                $"服务端 {cfg.ComponentName} 房间业务组件 Handle。\n" +
                $"实现 IInitializableRoomComponent，由 ServerRoomAssembler 装配管线统一管理生命周期。\n" +
                $"StableComponentId = \"{cfg.StableComponentId}\"", 0));

            sb.AppendLine($"public sealed class {className} : ServerRoomAssembler.IInitializableRoomComponent");
            sb.AppendLine("{");

            // StableComponentId
            sb.AppendLine($"    public const string StableComponentId = \"{cfg.StableComponentId}\";");
            sb.AppendLine("    public string ComponentId => StableComponentId;");
            sb.AppendLine();

            // 字段
            sb.AppendLine("    private readonly ServerRoomMessageSender _roomSender;");
            sb.AppendLine("    private readonly SessionManager _sessionManager;");
            sb.AppendLine($"    private {modelClass} _model;");
            sb.AppendLine("    private RoomInstance _room;");
            sb.AppendLine("    private bool _isInitialized;");
            sb.AppendLine();

            // 构造器
            sb.AppendLine($"    public {className}(");
            sb.AppendLine("        ServerRoomMessageSender roomSender,");
            sb.AppendLine("        SessionManager sessionManager)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (roomSender == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：roomSender 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (sessionManager == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] 构造失败：sessionManager 为 null。\");");
            sb.AppendLine("            return;");
            sb.AppendLine("        }");
            sb.AppendLine("        _roomSender = roomSender;");
            sb.AppendLine("        _sessionManager = sessionManager;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Init
            sb.AppendLine("    public bool Init(RoomInstance roomInstance)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (roomInstance == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] Init 失败：roomInstance 为 null。\");");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine("        _room = roomInstance;");
            sb.AppendLine($"        _model = new {modelClass}();");
            if (cfg.GenServiceLocatorStub)
            {
                sb.AppendLine("        // 将服务注册到房间作用域，供其他组件通过接口访问");
                sb.AppendLine("        // _room.RoomServiceLocator.Register<I???Service>(this);");
            }

            if (cfg.GenEventBusStub)
            {
                sb.AppendLine("        // 订阅房间域事件");
                sb.AppendLine("        // _room.EventBus.Subscribe<SomeRoomEvent>(OnSomeRoomEvent);");
            }

            sb.AppendLine("        _isInitialized = true;");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Deinit
            sb.AppendLine("    public void Deinit()");
            sb.AppendLine("    {");
            if (cfg.GenServiceLocatorStub)
            {
                sb.AppendLine("        // _room?.RoomServiceLocator.Unregister<I???Service>();");
            }

            if (cfg.GenEventBusStub)
            {
                sb.AppendLine("        // _room?.EventBus.Unsubscribe<SomeRoomEvent>(OnSomeRoomEvent);");
            }

            sb.AppendLine("        _model = null;");
            sb.AppendLine("        _room = null;");
            sb.AppendLine("        _isInitialized = false;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // GetHandlerBindings
            sb.AppendLine(
                "    public System.Collections.Generic.IReadOnlyList<ServerRoomAssembler.RoomHandlerBinding> GetHandlerBindings()");
            sb.AppendLine("    {");
            sb.AppendLine("        return new System.Collections.Generic.List<ServerRoomAssembler.RoomHandlerBinding>");
            sb.AppendLine("        {");
            if (c2sProtos.Count > 0)
            {
                foreach (var p in c2sProtos)
                {
                    sb.AppendLine($"            new ServerRoomAssembler.RoomHandlerBinding");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                MessageType = typeof({p.ClassName}),");
                    sb.AppendLine($"                Handler = On{p.ClassName}");
                    sb.AppendLine("            },");
                }
            }
            else
            {
                sb.AppendLine("            // TODO: 添加协议处理绑定");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            // 生命周期回调桩
            if (cfg.GenLifecycleCallbackStubs)
            {
                foreach (var cb in new[]
                         {
                             "OnRoomCreate", "OnRoomWaitStart", "OnRoomStartGame", "OnRoomGameEnding", "OnRoomSettling",
                             "OnRoomDestroy"
                         })
                {
                    sb.AppendLine($"    public void {cb}() {{ }}");
                }

                sb.AppendLine("    public void OnTick(float deltaTime) { }");
                sb.AppendLine();
            }

            // 重连快照补发桩
            if (cfg.GenReconnectSnapshotStub)
            {
                sb.AppendLine("    /// <summary>");
                sb.AppendLine("    /// 向重连成员补发当前状态快照，由 IRoomBaseSettingsService.NotifyReconnectRecovered 触发。");
                sb.AppendLine("    /// </summary>");
                sb.AppendLine("    public void SendSnapshotToMember(string sessionId)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (string.IsNullOrEmpty(sessionId)) return;");
                sb.AppendLine("        // TODO: 构建并发送快照");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // 协议处理方法桩
            foreach (var p in c2sProtos)
            {
                sb.AppendLine($"    private void On{p.ClassName}(");
                sb.AppendLine("        StellarNet.Shared.Identity.ConnectionId connectionId,");
                sb.AppendLine("        string roomId,");
                sb.AppendLine("        object rawMessage)");
                sb.AppendLine("    {");
                sb.AppendLine($"        if (!_isInitialized)");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            UnityEngine.Debug.LogError($\"[{className}] On{p.ClassName} 失败：组件未初始化，RoomId={{roomId}}。\");");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine($"        var message = rawMessage as {p.ClassName};");
                sb.AppendLine("        if (message == null)");
                sb.AppendLine("        {");
                sb.AppendLine(
                    $"            UnityEngine.Debug.LogError($\"[{className}] On{p.ClassName} 类型转换失败，RoomId={{roomId}}。\");");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine("        // TODO: 实现业务逻辑");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 房间组件 Handle（客户端）────────────────────────────────

        /// <summary>
        /// 生成客户端房间组件 Handle 类的完整代码体（不含 namespace 包装）。
        /// 实现 IInitializableClientRoomComponent，包含全部生命周期回调桩。
        /// </summary>
        public static string BuildClientRoomHandleBody(RoomComponentConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ClientHandleClassName;
            string modelClass = cfg.ClientModelClassName;

            var s2cProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.S2C_Room)
                    s2cProtos.Add(p);
            }

            sb.Append(BuildSummary(
                $"客户端 {cfg.ComponentName} 房间业务组件 Handle。\n" +
                $"实现 IInitializableClientRoomComponent，由 ClientRoomAssembler 装配管线统一管理生命周期。\n" +
                $"StableComponentId = \"{cfg.StableComponentId}\"", 0));

            sb.AppendLine($"public sealed class {className} : ClientRoomAssembler.IInitializableClientRoomComponent");
            sb.AppendLine("{");

            sb.AppendLine($"    public const string StableComponentId = \"{cfg.StableComponentId}\";");
            sb.AppendLine("    public string ComponentId => StableComponentId;");
            sb.AppendLine();

            sb.AppendLine($"    private {modelClass} _model;");
            sb.AppendLine("    private ClientRoomInstance _room;");
            sb.AppendLine();

            // 事件
            if (s2cProtos.Count > 0)
            {
                sb.AppendLine("    // 供 View 层订阅的业务事件");
                foreach (var p in s2cProtos)
                {
                    string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                    sb.AppendLine($"    public event System.Action<{p.ClassName}> {evtName};");
                }

                sb.AppendLine();
            }

            // Init
            sb.AppendLine("    public bool Init(ClientRoomInstance roomInstance)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (roomInstance == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[{className}] Init 失败：roomInstance 为 null。\");");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine("        _room = roomInstance;");
            sb.AppendLine($"        _model = new {modelClass}();");
            sb.AppendLine("        _room.RoomServiceLocator.Register(this);");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Deinit
            sb.AppendLine("    public void Deinit()");
            sb.AppendLine("    {");
            sb.AppendLine("        _room?.RoomServiceLocator.Unregister<" + className + ">();");
            sb.AppendLine("        _model = null;");
            sb.AppendLine("        _room = null;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // GetHandlerBindings
            sb.AppendLine(
                "    public System.Collections.Generic.IReadOnlyList<ClientRoomAssembler.ClientRoomHandlerBinding> GetHandlerBindings()");
            sb.AppendLine("    {");
            sb.AppendLine(
                "        return new System.Collections.Generic.List<ClientRoomAssembler.ClientRoomHandlerBinding>");
            sb.AppendLine("        {");
            if (s2cProtos.Count > 0)
            {
                foreach (var p in s2cProtos)
                {
                    sb.AppendLine("            new ClientRoomAssembler.ClientRoomHandlerBinding");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                MessageType = typeof({p.ClassName}),");
                    sb.AppendLine($"                Handler = On{p.ClassName}Raw");
                    sb.AppendLine("            },");
                }
            }
            else
            {
                sb.AppendLine("            // TODO: 添加协议处理绑定");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            // 生命周期
            sb.AppendLine("    public void OnTick(float deltaTime) { }");
            sb.AppendLine("    public void OnRoomDestroy() { _model = null; }");
            sb.AppendLine();

            // 协议处理方法桩
            foreach (var p in s2cProtos)
            {
                string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                sb.AppendLine($"    private void On{p.ClassName}Raw(string roomId, object rawMessage)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var message = rawMessage as {p.ClassName};");
                sb.AppendLine("        if (message == null) return;");
                sb.AppendLine("        // TODO: 更新 Model 状态");
                sb.AppendLine($"        {evtName}?.Invoke(message);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 房间组件 Model ────────────────────────────────────────

        /// <summary>
        /// 生成房间组件 Model 类的完整代码体（服务端/客户端通用模板）。
        /// </summary>
        public static string BuildRoomModelBody(string className, string componentName, string side)
        {
            var sb = new StringBuilder();
            sb.Append(BuildSummary(
                $"{side} {componentName} 房间组件 Model，维护组件运行时状态。\n" +
                $"不承载业务逻辑，不直接驱动任何网络发送。", 0));

            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 添加状态字段");
            sb.AppendLine();
            sb.AppendLine($"    public {className}() {{ }}");
            sb.AppendLine();
            sb.AppendLine("    public void Clear()");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 清空状态");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 协议聚合文件 ──────────────────────────────────────────

        /// <summary>
        /// 生成协议聚合文件的完整代码体（不含 namespace 包装）。
        /// 包含文件级注释块与所有协议类定义。
        /// </summary>
        public static string BuildProtoFileBody(
            string moduleName,
            string domain,
            int startId,
            int endId,
            List<ProtoDefinition> protos)
        {
            var sb = new StringBuilder();

            // 文件级注释块
            sb.AppendLine("// ────────────────────────────────────────────────────────────────");
            sb.AppendLine($"// {moduleName} 模块协议聚合脚本");
            sb.AppendLine($"// 号段：{startId} - {endId}");
            sb.AppendLine($"// 域归属：{domain}");
            sb.AppendLine("// ────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            foreach (var proto in protos)
            {
                sb.Append(BuildProtoClass(proto));
            }

            return sb.ToString();
        }
    }
}