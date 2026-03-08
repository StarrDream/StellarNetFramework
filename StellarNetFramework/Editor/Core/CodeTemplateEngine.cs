// ════════════════════════════════════════════════════════════════
// 文件：CodeTemplateEngine.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/CodeTemplateEngine.cs
// 职责：代码模板引擎。
//       更新：修复命名空间引用缺失问题。
//       1. 协议文件自动引入 StellarNet.Shared.Protocol.Attributes
//       2. Handle/Model 自动引入 Config 中配置的 ProtoNamespace
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    public static class CodeTemplateEngine
    {
        // ── 基础方法保持不变 ──────────────────────────────────────
        public static string BuildFileHeader(string fileName, string description)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// ════════════════════════════════════════════════════════════════");
            sb.AppendLine($"// 此文件由 StellarNet 脚手架工具自动生成");
            sb.AppendLine($"// 文件：{fileName}");
            sb.AppendLine($"// 生成时间：{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"// 说明：{description}");
            sb.AppendLine("// ════════════════════════════════════════════════════════════════");
            sb.AppendLine();
            return sb.ToString();
        }

        public static string BuildUsings(IEnumerable<string> namespaces)
        {
            var sorted = new SortedSet<string>(namespaces);
            var sb = new StringBuilder();
            foreach (var ns in sorted)
            {
                // 过滤空命名空间
                if (!string.IsNullOrWhiteSpace(ns))
                {
                    sb.AppendLine($"using {ns};");
                }
            }

            sb.AppendLine();
            return sb.ToString();
        }

        public static string WrapInNamespace(string namespaceName, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            foreach (var line in body.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                if (string.IsNullOrEmpty(trimmed)) sb.AppendLine();
                else sb.AppendLine($"    {trimmed}");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

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

        // ── 协议生成优化 ──────────────────────────────────────────
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

        public static string BuildProtoClass(ProtoDefinition proto)
        {
            var sb = new StringBuilder();
            string baseClass = GetProtoBaseClass(proto.Direction);

            // 修复：确保注释不为空
            string comment = string.IsNullOrEmpty(proto.Comment)
                ? $"{proto.ClassName}"
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

            sb.AppendLine("}");
            sb.AppendLine();
            return sb.ToString();
        }

        // ── 协议聚合文件生成（修复命名空间） ──────────────────────
        public static string BuildProtoFileBody(
            string moduleName,
            string domain,
            int startId,
            int endId,
            List<ProtoDefinition> protos)
        {
            var sb = new StringBuilder();
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

        // ── 客户端 Handle 生成（修复命名空间） ────────────────────
        public static string BuildClientGlobalHandleBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ClientHandleClassName;
            string modelClass = cfg.ClientModelClassName;

            var s2cProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.S2C_Global)
                    s2cProtos.Add(p);
            }

            sb.Append(BuildSummary($"客户端 {cfg.ModuleName} Handle。", 0));
            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine($"    private readonly {modelClass} _model;");
            sb.AppendLine("    private readonly ClientGlobalMessageRegistrar _registrar;");
            sb.AppendLine();

            // 事件定义
            if (s2cProtos.Count > 0)
            {
                foreach (var p in s2cProtos)
                {
                    string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                    sb.AppendLine($"    public event System.Action<{p.ClassName}> {evtName};");
                }

                sb.AppendLine();
            }

            // 构造函数
            sb.AppendLine($"    public {className}(");
            sb.AppendLine($"        {modelClass} model,");
            sb.AppendLine("        ClientGlobalMessageRegistrar registrar)");
            sb.AppendLine("    {");
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
                foreach (var p in s2cProtos)
                {
                    sb.AppendLine();
                    sb.Append($"            .Register<{p.ClassName}>(On{p.ClassName})");
                }

                sb.AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // UnregisterAll
            sb.AppendLine("    public void UnregisterAll()");
            sb.AppendLine("    {");
            if (s2cProtos.Count > 0)
            {
                sb.Append("        _registrar");
                foreach (var p in s2cProtos)
                {
                    sb.AppendLine();
                    sb.Append($"            .Unregister<{p.ClassName}>()");
                }

                sb.AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // Handlers
            foreach (var p in s2cProtos)
            {
                string handlerName = $"On{p.ClassName}";
                string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                sb.AppendLine($"    private void {handlerName}({p.ClassName} message)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (message == null) return;");
                sb.AppendLine($"        {evtName}?.Invoke(message);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 服务端 Handle 生成（修复命名空间） ────────────────────
        public static string BuildServerGlobalHandleBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            string className = cfg.ServerHandleClassName;
            string modelClass = cfg.ServerModelClassName;

            var c2sProtos = new List<ProtoDefinition>();
            foreach (var p in cfg.Protocols)
            {
                if (p.Direction == ProtoDirection.C2S_Global)
                    c2sProtos.Add(p);
            }

            string implements = cfg.GenServiceLocatorStub ? " : IGlobalService" : "";
            sb.Append(BuildSummary($"服务端 {cfg.ModuleName} Handle。", 0));
            sb.AppendLine($"public sealed class {className}{implements}");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly SessionManager _sessionManager;");
            sb.AppendLine($"    private readonly {modelClass} _model;");
            sb.AppendLine("    private readonly ServerGlobalMessageSender _globalSender;");
            sb.AppendLine("    private readonly GlobalMessageRegistrar _registrar;");
            sb.AppendLine();

            sb.AppendLine($"    public {className}(");
            sb.AppendLine("        SessionManager sessionManager,");
            sb.AppendLine($"        {modelClass} model,");
            sb.AppendLine("        ServerGlobalMessageSender globalSender,");
            sb.AppendLine("        GlobalMessageRegistrar registrar)");
            sb.AppendLine("    {");
            sb.AppendLine("        _sessionManager = sessionManager;");
            sb.AppendLine("        _model = model;");
            sb.AppendLine("        _globalSender = globalSender;");
            sb.AppendLine("        _registrar = registrar;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void RegisterAll()");
            sb.AppendLine("    {");
            if (c2sProtos.Count > 0)
            {
                sb.Append("        _registrar");
                foreach (var p in c2sProtos)
                {
                    sb.AppendLine();
                    sb.Append($"            .Register<{p.ClassName}>(On{p.ClassName})");
                }

                sb.AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void UnregisterAll()");
            sb.AppendLine("    {");
            if (c2sProtos.Count > 0)
            {
                sb.Append("        _registrar");
                foreach (var p in c2sProtos)
                {
                    sb.AppendLine();
                    sb.Append($"            .Unregister<{p.ClassName}>()");
                }

                sb.AppendLine(";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            foreach (var p in c2sProtos)
            {
                sb.AppendLine($"    private void On{p.ClassName}(ConnectionId connectionId, {p.ClassName} message)");
                sb.AppendLine("    {");
                sb.AppendLine("        // TODO: 实现业务逻辑");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── 房间组件 Handle 生成（修复命名空间） ──────────────────
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

            sb.Append(BuildSummary($"服务端 {cfg.ComponentName} 房间组件 Handle。", 0));
            sb.AppendLine($"public sealed class {className} : ServerRoomAssembler.IInitializableRoomComponent");
            sb.AppendLine("{");
            sb.AppendLine($"    public const string StableComponentId = \"{cfg.StableComponentId}\";");
            sb.AppendLine("    public string ComponentId => StableComponentId;");
            sb.AppendLine();
            sb.AppendLine("    private readonly ServerRoomMessageSender _roomSender;");
            sb.AppendLine("    private readonly SessionManager _sessionManager;");
            sb.AppendLine($"    private {modelClass} _model;");
            sb.AppendLine("    private RoomInstance _room;");
            sb.AppendLine();

            sb.AppendLine($"    public {className}(");
            sb.AppendLine("        ServerRoomMessageSender roomSender,");
            sb.AppendLine("        SessionManager sessionManager)");
            sb.AppendLine("    {");
            sb.AppendLine("        _roomSender = roomSender;");
            sb.AppendLine("        _sessionManager = sessionManager;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public bool Init(RoomInstance roomInstance)");
            sb.AppendLine("    {");
            sb.AppendLine("        _room = roomInstance;");
            sb.AppendLine($"        _model = new {modelClass}();");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void Deinit()");
            sb.AppendLine("    {");
            sb.AppendLine("        _model = null;");
            sb.AppendLine("        _room = null;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine(
                "    public System.Collections.Generic.IReadOnlyList<ServerRoomAssembler.RoomHandlerBinding> GetHandlerBindings()");
            sb.AppendLine("    {");
            sb.AppendLine("        return new System.Collections.Generic.List<ServerRoomAssembler.RoomHandlerBinding>");
            sb.AppendLine("        {");
            foreach (var p in c2sProtos)
            {
                sb.AppendLine("            new ServerRoomAssembler.RoomHandlerBinding");
                sb.AppendLine("            {");
                sb.AppendLine($"                MessageType = typeof({p.ClassName}),");
                sb.AppendLine($"                Handler = On{p.ClassName}");
                sb.AppendLine("            },");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            // 生命周期桩
            sb.AppendLine("    public void OnRoomCreate() { }");
            sb.AppendLine("    public void OnRoomWaitStart() { }");
            sb.AppendLine("    public void OnRoomStartGame() { }");
            sb.AppendLine("    public void OnRoomGameEnding() { }");
            sb.AppendLine("    public void OnRoomSettling() { }");
            sb.AppendLine("    public void OnTick(float deltaTime) { }");
            sb.AppendLine("    public void OnRoomDestroy() { }");
            sb.AppendLine();

            // 协议处理桩
            foreach (var p in c2sProtos)
            {
                sb.AppendLine($"    private void On{p.ClassName}(");
                sb.AppendLine("        StellarNet.Shared.Identity.ConnectionId connectionId,");
                sb.AppendLine("        string roomId,");
                sb.AppendLine("        object rawMessage)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var message = rawMessage as {p.ClassName};");
                sb.AppendLine("        if (message == null) return;");
                sb.AppendLine("        // TODO: 业务逻辑");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ── Model 生成（保持简单） ────────────────────────────────
        public static string BuildClientGlobalModelBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public sealed class {cfg.ClientModelClassName}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 状态数据");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string BuildServerGlobalModelBody(GlobalModuleConfig cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public sealed class {cfg.ServerModelClassName}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 状态数据");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string BuildRoomModelBody(string className, string componentName, string side)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public sealed class {className}");
            sb.AppendLine("{");
            sb.AppendLine("    // TODO: 状态数据");
            sb.AppendLine("}");
            return sb.ToString();
        }

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

            sb.Append(BuildSummary($"客户端 {cfg.ComponentName} 房间组件 Handle。", 0));
            sb.AppendLine($"public sealed class {className} : ClientRoomAssembler.IInitializableClientRoomComponent");
            sb.AppendLine("{");
            sb.AppendLine($"    public const string StableComponentId = \"{cfg.StableComponentId}\";");
            sb.AppendLine("    public string ComponentId => StableComponentId;");
            sb.AppendLine();
            sb.AppendLine($"    private {modelClass} _model;");
            sb.AppendLine("    private ClientRoomInstance _room;");
            sb.AppendLine();

            foreach (var p in s2cProtos)
            {
                string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                sb.AppendLine($"    public event System.Action<{p.ClassName}> {evtName};");
            }

            sb.AppendLine();

            sb.AppendLine("    public bool Init(ClientRoomInstance roomInstance)");
            sb.AppendLine("    {");
            sb.AppendLine("        _room = roomInstance;");
            sb.AppendLine($"        _model = new {modelClass}();");
            sb.AppendLine("        return true;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void Deinit()");
            sb.AppendLine("    {");
            sb.AppendLine("        _model = null;");
            sb.AppendLine("        _room = null;");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine(
                "    public System.Collections.Generic.IReadOnlyList<ClientRoomAssembler.ClientRoomHandlerBinding> GetHandlerBindings()");
            sb.AppendLine("    {");
            sb.AppendLine(
                "        return new System.Collections.Generic.List<ClientRoomAssembler.ClientRoomHandlerBinding>");
            sb.AppendLine("        {");
            foreach (var p in s2cProtos)
            {
                sb.AppendLine("            new ClientRoomAssembler.ClientRoomHandlerBinding");
                sb.AppendLine("            {");
                sb.AppendLine($"                MessageType = typeof({p.ClassName}),");
                sb.AppendLine($"                Handler = On{p.ClassName}Raw");
                sb.AppendLine("            },");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    public void OnTick(float deltaTime) { }");
            sb.AppendLine("    public void OnRoomDestroy() { }");
            sb.AppendLine();

            foreach (var p in s2cProtos)
            {
                string evtName = $"On{p.ClassName.Replace("S2C_", "")}";
                sb.AppendLine($"    private void On{p.ClassName}Raw(string roomId, object rawMessage)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var message = rawMessage as {p.ClassName};");
                sb.AppendLine("        if (message == null) return;");
                sb.AppendLine($"        {evtName}?.Invoke(message);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}