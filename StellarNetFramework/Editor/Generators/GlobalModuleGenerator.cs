// ════════════════════════════════════════════════════════════════
// 文件：GlobalModuleGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/GlobalModuleGenerator.cs
// 职责：全局模块代码生成器。
//       更新：
//       1. ServerHandle 增加 StellarNet.Server.Sender 引用。
//       2. ProtoFile 移除多余的 Attributes 引用。
// ════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    public sealed class GlobalModuleGenerator
    {
        private readonly FileWriteService _fileWriteService;
        public GlobalModuleGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        public void Generate(GlobalModuleConfig config, GenerateResult result)
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

        private void GenerateProtoFile(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ProtoFileName}.cs", $"{config.ModuleName} 协议定义"));
            
            // 修复：移除 Attributes 引用，只保留 Shared.Protocol
            var usings = new List<string> { 
                "StellarNet.Shared.Protocol"
            };
            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            int minId = 10000, maxId = 10000; 
            if (config.Protocols.Count > 0)
            {
                minId = config.Protocols[0].MessageId;
                maxId = config.Protocols[config.Protocols.Count - 1].MessageId;
            }

            string body = CodeTemplateEngine.BuildProtoFileBody(config.ModuleName, "Global", minId, maxId, config.Protocols);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ProtoNamespace, body));
            
            _fileWriteService.Enqueue($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs", sb.ToString(), result);
        }

        private void GenerateServerHandle(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ServerHandleClassName}.cs", "服务端 Handle"));
            
            // 修复：添加 StellarNet.Server.Sender
            var usings = new List<string>
            {
                "StellarNet.Server.Network",
                "StellarNet.Server.Session",
                "StellarNet.Server.Sender", // <--- 关键修复：引用发送器命名空间
                "StellarNet.Shared.Identity",
                "UnityEngine",
                config.ProtoNamespace
            };
            if (config.GenServiceLocatorStub) usings.Add("StellarNet.Server.Service");
            
            sb.Append(CodeTemplateEngine.BuildUsings(usings));
            string body = CodeTemplateEngine.BuildServerGlobalHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));
            
            _fileWriteService.Enqueue($"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerHandleClassName}.cs", sb.ToString(), result);
        }

        private void GenerateClientHandle(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ClientHandleClassName}.cs", "客户端 Handle"));
            
            var usings = new List<string>
            {
                "System",
                "StellarNet.Client.Network",
                "StellarNet.Client.Session",
                "UnityEngine",
                config.ProtoNamespace
            };
            
            sb.Append(CodeTemplateEngine.BuildUsings(usings));
            string body = CodeTemplateEngine.BuildClientGlobalHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));
            
            _fileWriteService.Enqueue($"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientHandleClassName}.cs", sb.ToString(), result);
        }

        private void GenerateServerModel(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ServerModelClassName}.cs", "服务端 Model"));
            sb.Append(CodeTemplateEngine.BuildUsings(new[] { "System.Collections.Generic" }));
            string body = CodeTemplateEngine.BuildServerGlobalModelBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));
            _fileWriteService.Enqueue($"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerModelClassName}.cs", sb.ToString(), result);
        }

        private void GenerateClientModel(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader($"{config.ClientModelClassName}.cs", "客户端 Model"));
            sb.Append(CodeTemplateEngine.BuildUsings(new[] { "System.Collections.Generic" }));
            string body = CodeTemplateEngine.BuildClientGlobalModelBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));
            _fileWriteService.Enqueue($"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientModelClassName}.cs", sb.ToString(), result);
        }
    }
}
