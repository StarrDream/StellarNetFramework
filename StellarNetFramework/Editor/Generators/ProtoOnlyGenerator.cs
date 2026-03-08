// ════════════════════════════════════════════════════════════════
// 文件：ProtoOnlyGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/ProtoOnlyGenerator.cs
// 职责：纯协议文件生成器。
//       更新：ProtoFile 移除多余的 Attributes 引用。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    public sealed class ProtoOnlyGenerator
    {
        private readonly FileWriteService _fileWriteService;

        public ProtoOnlyGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        public void Generate(
            string fileName,
            string outputRelativePath,
            string protoNamespace,
            string moduleName,
            string domain,
            List<ProtoDefinition> protos,
            GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                result.AddError("[ProtoOnlyGenerator] Generate 失败：fileName 为空。");
                return;
            }
            // ... (其他校验逻辑保持不变，已在 Validator 或外部处理)

            int minId = int.MaxValue;
            int maxId = int.MinValue;
            foreach (var p in protos)
            {
                if (p.MessageId < minId) minId = p.MessageId;
                if (p.MessageId > maxId) maxId = p.MessageId;
            }

            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{fileName}.cs",
                $"{moduleName} 协议聚合文件，包含全部上下行协议定义。由协议定义面板独立生成。"));

            // 修复：移除 Attributes 引用
            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "StellarNet.Shared.Protocol"
            }));

            string body = CodeTemplateEngine.BuildProtoFileBody(
                moduleName,
                domain,
                minId,
                maxId,
                protos);

            sb.Append(CodeTemplateEngine.WrapInNamespace(protoNamespace, body));

            string relativePath = $"{outputRelativePath}/{fileName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }
    }
}