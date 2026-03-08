// ════════════════════════════════════════════════════════════════
// 文件：RoomComponentGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/RoomComponentGenerator.cs
// 职责：房间组件代码生成器。
//       更新：
//       1. ServerHandle 增加 StellarNet.Server.Sender 引用。
//       2. ProtoFile 移除多余的 Attributes 引用。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    public sealed class RoomComponentGenerator
    {
        private readonly FileWriteService _fileWriteService;

        public RoomComponentGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        public void Generate(RoomComponentConfig config, GenerateResult result)
        {
            if (config == null) return;

            if (config.GenProtoFile) GenerateProtoFile(config, result);

            bool needServer = config.Target == GenerateTarget.BothSides || config.Target == GenerateTarget.ServerOnly;
            bool needClient = config.Target == GenerateTarget.BothSides || config.Target == GenerateTarget.ClientOnly;

            if (needServer)
            {
                if (config.GenServerModel) GenerateServerModel(config, result);
                if (config.GenServerHandle) GenerateServerHandle(config, result);
            }

            if (needClient)
            {
                if (config.GenClientModel) GenerateClientModel(config, result);
                if (config.GenClientHandle) GenerateClientHandle(config, result);
            }
        }

        private void GenerateProtoFile(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ProtoFileName}.cs", $"{config.ComponentName} 协议定义"));

            // 修复：移除 Attributes 引用
            var usings = new List<string>
            {
                "StellarNet.Shared.Protocol"
            };
            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            int minId = 10000, maxId = 10000;
            if (config.Protocols.Count > 0)
            {
                minId = config.Protocols[0].MessageId;
                maxId = config.Protocols[config.Protocols.Count - 1].MessageId;
            }

            string body =
                CodeTemplateEngine.BuildProtoFileBody(config.ComponentName, "Room", minId, maxId, config.Protocols);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ProtoNamespace, body));

            _fileWriteService.Enqueue($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs", sb.ToString(), result);
        }

        private void GenerateServerHandle(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ServerHandleClassName}.cs", "服务端房间组件 Handle"));

            // 修复：添加 StellarNet.Server.Sender
            var usings = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "StellarNet.Server.Network",
                "StellarNet.Server.Room",
                "StellarNet.Server.Session",
                "StellarNet.Server.Sender", // <--- 关键修复：引用发送器命名空间
                "StellarNet.Shared.Identity",
                "UnityEngine",
                config.ProtoNamespace
            };
            if (config.GenServiceLocatorStub) usings.Add("StellarNet.Server.Service");
            if (config.GenEventBusStub) usings.Add("StellarNet.Server.Room.Events");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));
            string body = CodeTemplateEngine.BuildServerRoomHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));

            _fileWriteService.Enqueue(
                $"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerHandleClassName}.cs", sb.ToString(),
                result);
        }

        private void GenerateClientHandle(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ClientHandleClassName}.cs", "客户端房间组件 Handle"));

            var usings = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "StellarNet.Client.Network",
                "StellarNet.Client.Room",
                "StellarNet.Shared.Identity",
                "UnityEngine",
                config.ProtoNamespace
            };
            if (config.GenServiceLocatorStub) usings.Add("StellarNet.Client.Service");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));
            string body = CodeTemplateEngine.BuildClientRoomHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));

            _fileWriteService.Enqueue(
                $"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientHandleClassName}.cs", sb.ToString(),
                result);
        }

        private void GenerateServerModel(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ServerModelClassName}.cs", "服务端房间组件 Model"));
            sb.Append(CodeTemplateEngine.BuildUsings(new[] { "System.Collections.Generic" }));
            string body =
                CodeTemplateEngine.BuildRoomModelBody(config.ServerModelClassName, config.ComponentName, "服务端");
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));
            _fileWriteService.Enqueue(
                $"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerModelClassName}.cs", sb.ToString(),
                result);
        }

        private void GenerateClientModel(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ClientModelClassName}.cs", "客户端房间组件 Model"));
            sb.Append(CodeTemplateEngine.BuildUsings(new[] { "System.Collections.Generic" }));
            string body =
                CodeTemplateEngine.BuildRoomModelBody(config.ClientModelClassName, config.ComponentName, "客户端");
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));
            _fileWriteService.Enqueue(
                $"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientModelClassName}.cs", sb.ToString(),
                result);
        }
    }
}