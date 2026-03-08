// ════════════════════════════════════════════════════════════════
// 文件：GlobalModuleGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/GlobalModuleGenerator.cs
// 职责：全局模块代码生成器。
//       接收 GlobalModuleConfig，调用 CodeTemplateEngine 构建代码字符串，
//       再通过 FileWriteService 入队写入，不直接操作任何磁盘 IO。
//       生成顺序：协议文件 → 服务端 Model → 服务端 Handle → 客户端 Model → 客户端 Handle。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 全局模块生成器，负责将 GlobalModuleConfig 转换为磁盘上的 .cs 文件集合。
    /// 生成器本身无状态，可安全复用。
    /// </summary>
    public sealed class GlobalModuleGenerator
    {
        private readonly FileWriteService _fileWriteService;

        public GlobalModuleGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        // ── 入口 ──────────────────────────────────────────────────

        /// <summary>
        /// 执行全局模块的完整生成流程。
        /// 根据 config 中的生成选项决定生成哪些文件，所有文件入队后由调用方统一 FlushAll。
        /// </summary>
        public void Generate(GlobalModuleConfig config, GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[GlobalModuleGenerator] Generate 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.ModuleName))
            {
                result.AddError("[GlobalModuleGenerator] Generate 失败：ModuleName 为空。");
                return;
            }

            // 校验协议 ID 唯一性，在生成前阻断重复 ID，防止框架启动阶段 MessageRegistry 报错
            if (!ValidateProtocolIds(config.Protocols, result))
                return;

            // 按顺序生成各文件，顺序本身无强制依赖，但先生成协议文件
            // 方便开发者在 Handle 生成后立刻能看到完整的协议引用
            if (config.GenProtoFile)
                GenerateProtoFile(config, result);

            bool needServer = config.Target == GenerateTarget.BothSides
                              || config.Target == GenerateTarget.ServerOnly;

            bool needClient = config.Target == GenerateTarget.BothSides
                              || config.Target == GenerateTarget.ClientOnly;

            if (needServer)
            {
                if (config.GenServerModel)
                    GenerateServerModel(config, result);

                if (config.GenServerHandle)
                    GenerateServerHandle(config, result);
            }

            if (needClient)
            {
                if (config.GenClientModel)
                    GenerateClientModel(config, result);

                if (config.GenClientHandle)
                    GenerateClientHandle(config, result);
            }
        }

        // ── 协议文件 ──────────────────────────────────────────────

        private void GenerateProtoFile(GlobalModuleConfig config, GenerateResult result)
        {
            if (config.Protocols == null || config.Protocols.Count == 0)
            {
                result.AddWarning($"[GlobalModuleGenerator] {config.ModuleName} 协议列表为空，跳过协议文件生成。");
                return;
            }

            int minId = int.MaxValue;
            int maxId = int.MinValue;
            foreach (var p in config.Protocols)
            {
                if (p.MessageId < minId) minId = p.MessageId;
                if (p.MessageId > maxId) maxId = p.MessageId;
            }

            // 构建协议文件内容
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ProtoFileName}.cs",
                $"{config.ModuleName} 模块协议聚合文件，包含全部上下行协议定义。"));

            // 收集所有协议需要的 using
            var usings = new List<string>
            {
                "StellarNet.Shared.Protocol",
                "StellarNet.Shared.Protocol.Attributes"
            };
            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            string body = CodeTemplateEngine.BuildProtoFileBody(
                config.ModuleName,
                "Global（全局域）",
                minId,
                maxId,
                config.Protocols);

            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ProtoNamespace, body));

            string relativePath = $"{config.ProtoOutputPath}/{config.ProtoFileName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 服务端 Model ──────────────────────────────────────────

        private void GenerateServerModel(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ServerModelClassName}.cs",
                $"{config.ModuleName} 服务端 Model，维护服务端运行时状态。"));

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "System.Collections.Generic"
            }));

            string body = CodeTemplateEngine.BuildServerGlobalModelBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));

            string relativePath = $"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerModelClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 服务端 Handle ─────────────────────────────────────────

        private void GenerateServerHandle(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ServerHandleClassName}.cs",
                $"{config.ModuleName} 服务端 Handle，处理客户端上行协议并驱动 Model 状态变更。"));

            // 收集服务端 Handle 需要的 using
            var usings = new List<string>
            {
                "StellarNet.Server.Network",
                "StellarNet.Server.Session",
                "StellarNet.Shared.Identity",
                config.ProtoNamespace,
                "UnityEngine"
            };

            if (config.GenServiceLocatorStub)
                usings.Add("StellarNet.Server.Service");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            string body = CodeTemplateEngine.BuildServerGlobalHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));

            string relativePath = $"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerHandleClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 客户端 Model ──────────────────────────────────────────

        private void GenerateClientModel(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ClientModelClassName}.cs",
                $"{config.ModuleName} 客户端 Model，维护本地运行时状态。"));

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "System.Collections.Generic"
            }));

            string body = CodeTemplateEngine.BuildClientGlobalModelBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));

            string relativePath = $"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientModelClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 客户端 Handle ─────────────────────────────────────────

        private void GenerateClientHandle(GlobalModuleConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ClientHandleClassName}.cs",
                $"{config.ModuleName} 客户端 Handle，处理服务端下行协议并驱动 Model 状态变更。"));

            var usings = new List<string>
            {
                "System",
                "StellarNet.Client.Network",
                "StellarNet.Client.Session",
                config.ProtoNamespace,
                "UnityEngine"
            };

            if (config.GenServiceLocatorStub)
                usings.Add("StellarNet.Client.Service");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            string body = CodeTemplateEngine.BuildClientGlobalHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));

            string relativePath = $"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientHandleClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 协议 ID 校验 ──────────────────────────────────────────

        /// <summary>
        /// 校验协议列表中是否存在重复 ID 或非法 ID（框架保留段 0-9999）。
        /// 在生成前阻断，比在运行时被 MessageRegistry 报错更早发现问题。
        /// </summary>
        private bool ValidateProtocolIds(
            List<ProtoDefinition> protos,
            GenerateResult result)
        {
            if (protos == null || protos.Count == 0)
                return true;

            var seen = new HashSet<int>();
            bool valid = true;

            foreach (var p in protos)
            {
                if (p.MessageId < 10000)
                {
                    result.AddError($"[GlobalModuleGenerator] 协议 ID {p.MessageId}（{p.ClassName}）" +
                                    $"位于框架保留号段 0-9999，请使用 10000 以上的 ID。");
                    valid = false;
                }

                if (!seen.Add(p.MessageId))
                {
                    result.AddError($"[GlobalModuleGenerator] 协议 ID {p.MessageId} 重复，" +
                                    $"请检查协议列表中是否存在同 ID 的协议。");
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(p.ClassName))
                {
                    result.AddError($"[GlobalModuleGenerator] 协议 ID {p.MessageId} 的 ClassName 为空，" +
                                    $"无法生成有效的类定义。");
                    valid = false;
                }
            }

            return valid;
        }
    }
}