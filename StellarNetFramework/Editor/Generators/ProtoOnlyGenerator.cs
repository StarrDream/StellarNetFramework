// ════════════════════════════════════════════════════════════════
// 文件：ProtoOnlyGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/ProtoOnlyGenerator.cs
// 职责：纯协议文件生成器，独立于全局模块与房间组件生成器。
//       适用于先规划协议再实现业务的开发流程。
//       支持混合域协议（同一文件中同时包含 Global 与 Room 域协议），
//       但会在生成前输出 Warning 提示开发者注意域隔离原则。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 纯协议文件生成器，只生成协议聚合 .cs 文件，不生成任何 Handle 或 Model。
    /// 生成器本身无状态，可安全复用。
    /// </summary>
    public sealed class ProtoOnlyGenerator
    {
        private readonly FileWriteService _fileWriteService;

        public ProtoOnlyGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        // ── 入口 ──────────────────────────────────────────────────

        /// <summary>
        /// 执行纯协议文件生成。
        /// fileName：生成的 .cs 文件名（不含扩展名），例如 TurnSystemMessages。
        /// outputRelativePath：相对于 Assets/ 的输出目录，例如 Game/Shared/Protocol/Room。
        /// protoNamespace：协议类所在命名空间。
        /// moduleName：模块名，用于文件头注释。
        /// domain：域归属描述，用于文件头注释，例如 "Room（房间域）"。
        /// protos：协议定义列表。
        /// </summary>
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

            if (string.IsNullOrWhiteSpace(outputRelativePath))
            {
                result.AddError("[ProtoOnlyGenerator] Generate 失败：outputRelativePath 为空。");
                return;
            }

            if (string.IsNullOrWhiteSpace(protoNamespace))
            {
                result.AddError("[ProtoOnlyGenerator] Generate 失败：protoNamespace 为空。");
                return;
            }

            if (protos == null || protos.Count == 0)
            {
                result.AddError("[ProtoOnlyGenerator] Generate 失败：协议列表为空，无内容可生成。");
                return;
            }

            // 协议 ID 校验
            if (!ValidateProtocolIds(protos, result))
                return;

            // 域混合检查：同一文件中混合 Global 与 Room 域协议时发出警告
            CheckDomainMix(protos, fileName, result);

            // 计算号段范围
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

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "StellarNet.Shared.Protocol",
                "StellarNet.Shared.Protocol.Attributes"
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

        // ── 域混合检查 ────────────────────────────────────────────

        /// <summary>
        /// 检查协议列表中是否存在 Global 域与 Room 域混合的情况。
        /// 混合域协议会导致 MessageRegistry 的域隔离机制失效，
        /// 应当拆分为独立文件，此处仅警告不阻断，由开发者自行决策。
        /// </summary>
        private void CheckDomainMix(
            List<ProtoDefinition> protos,
            string fileName,
            GenerateResult result)
        {
            bool hasGlobal = false;
            bool hasRoom = false;

            foreach (var p in protos)
            {
                if (p.Direction == ProtoDirection.C2S_Global ||
                    p.Direction == ProtoDirection.S2C_Global)
                    hasGlobal = true;

                if (p.Direction == ProtoDirection.C2S_Room ||
                    p.Direction == ProtoDirection.S2C_Room)
                    hasRoom = true;
            }

            if (hasGlobal && hasRoom)
            {
                result.AddWarning(
                    $"[ProtoOnlyGenerator] 文件 {fileName} 中同时包含 Global 域与 Room 域协议。" +
                    $"建议拆分为独立文件以保证 MessageRegistry 域隔离机制正常工作。");
            }
        }

        // ── 协议 ID 校验 ──────────────────────────────────────────

        private bool ValidateProtocolIds(List<ProtoDefinition> protos, GenerateResult result)
        {
            var seen = new HashSet<int>();
            bool valid = true;

            foreach (var p in protos)
            {
                if (p.MessageId < 10000)
                {
                    result.AddError($"[ProtoOnlyGenerator] 协议 ID {p.MessageId}（{p.ClassName}）" +
                                    $"位于框架保留号段 0-9999，请使用 10000 以上的 ID。");
                    valid = false;
                }

                if (!seen.Add(p.MessageId))
                {
                    result.AddError($"[ProtoOnlyGenerator] 协议 ID {p.MessageId} 重复，" +
                                    $"请检查协议列表中是否存在同 ID 的协议。");
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(p.ClassName))
                {
                    result.AddError($"[ProtoOnlyGenerator] 协议 ID {p.MessageId} 的 ClassName 为空。");
                    valid = false;
                }
            }

            return valid;
        }
    }
}