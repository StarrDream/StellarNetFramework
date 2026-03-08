// ════════════════════════════════════════════════════════════════
// 文件：RoomComponentGenerator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Generators/RoomComponentGenerator.cs
// 职责：房间业务组件代码生成器。
//       接收 RoomComponentConfig，调用 CodeTemplateEngine 构建代码字符串，
//       再通过 FileWriteService 入队写入，不直接操作任何磁盘 IO。
//       生成顺序：协议文件 → 服务端 Model → 服务端 Handle → 客户端 Model → 客户端 Handle。
//       额外负责 StableComponentId 的格式校验，防止非法标识符进入注册表。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 房间业务组件生成器，负责将 RoomComponentConfig 转换为磁盘上的 .cs 文件集合。
    /// 生成器本身无状态，可安全复用。
    /// </summary>
    public sealed class RoomComponentGenerator
    {
        private readonly FileWriteService _fileWriteService;

        // StableComponentId 合法格式：小写字母、数字、点、下划线，例如 room.turn_system
        private static readonly Regex StableIdPattern =
            new Regex(@"^[a-z][a-z0-9._]*$", RegexOptions.Compiled);

        public RoomComponentGenerator(FileWriteService fileWriteService)
        {
            _fileWriteService = fileWriteService;
        }

        // ── 入口 ──────────────────────────────────────────────────

        /// <summary>
        /// 执行房间组件的完整生成流程。
        /// 根据 config 中的生成选项决定生成哪些文件，所有文件入队后由调用方统一 FlushAll。
        /// </summary>
        public void Generate(RoomComponentConfig config, GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[RoomComponentGenerator] Generate 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.ComponentName))
            {
                result.AddError("[RoomComponentGenerator] Generate 失败：ComponentName 为空。");
                return;
            }

            // StableComponentId 格式校验，非法格式会导致注册表键值冲突或序列化异常
            if (!ValidateStableComponentId(config.StableComponentId, result))
                return;

            // 协议 ID 唯一性校验
            if (!ValidateProtocolIds(config.Protocols, result))
                return;

            // 按顺序生成各文件
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

        private void GenerateProtoFile(RoomComponentConfig config, GenerateResult result)
        {
            if (config.Protocols == null || config.Protocols.Count == 0)
            {
                result.AddWarning($"[RoomComponentGenerator] {config.ComponentName} 协议列表为空，跳过协议文件生成。");
                return;
            }

            int minId = int.MaxValue;
            int maxId = int.MinValue;
            foreach (var p in config.Protocols)
            {
                if (p.MessageId < minId) minId = p.MessageId;
                if (p.MessageId > maxId) maxId = p.MessageId;
            }

            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ProtoFileName}.cs",
                $"{config.ComponentName} 房间组件协议聚合文件，包含全部上下行协议定义。"));

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "StellarNet.Shared.Protocol",
                "StellarNet.Shared.Protocol.Attributes"
            }));

            string body = CodeTemplateEngine.BuildProtoFileBody(
                config.ComponentName,
                "Room（房间域）",
                minId,
                maxId,
                config.Protocols);

            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ProtoNamespace, body));

            string relativePath = $"{config.ProtoOutputPath}/{config.ProtoFileName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 服务端 Model ──────────────────────────────────────────

        private void GenerateServerModel(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ServerModelClassName}.cs",
                $"{config.ComponentName} 服务端房间组件 Model。"));

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "System.Collections.Generic"
            }));

            string body = CodeTemplateEngine.BuildRoomModelBody(
                config.ServerModelClassName,
                config.ComponentName,
                "服务端");

            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));

            string relativePath = $"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerModelClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 服务端 Handle ─────────────────────────────────────────

        private void GenerateServerHandle(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ServerHandleClassName}.cs",
                $"{config.ComponentName} 服务端房间组件 Handle，实现 IInitializableRoomComponent。"));

            var usings = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "StellarNet.Server.Network",
                "StellarNet.Server.Room",
                "StellarNet.Server.Session",
                "StellarNet.Shared.Identity",
                config.ProtoNamespace,
                "UnityEngine"
            };

            if (config.GenServiceLocatorStub)
                usings.Add("StellarNet.Server.Service");

            if (config.GenEventBusStub)
                usings.Add("StellarNet.Server.Room.Events");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            string body = CodeTemplateEngine.BuildServerRoomHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ServerNamespace, body));

            string relativePath = $"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerHandleClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 客户端 Model ──────────────────────────────────────────

        private void GenerateClientModel(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ClientModelClassName}.cs",
                $"{config.ComponentName} 客户端房间组件 Model。"));

            sb.Append(CodeTemplateEngine.BuildUsings(new[]
            {
                "System.Collections.Generic"
            }));

            string body = CodeTemplateEngine.BuildRoomModelBody(
                config.ClientModelClassName,
                config.ComponentName,
                "客户端");

            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));

            string relativePath = $"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientModelClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── 客户端 Handle ─────────────────────────────────────────

        private void GenerateClientHandle(RoomComponentConfig config, GenerateResult result)
        {
            var sb = new StringBuilder();
            sb.Append(CodeTemplateEngine.BuildFileHeader(
                $"{config.ClientHandleClassName}.cs",
                $"{config.ComponentName} 客户端房间组件 Handle，实现 IInitializableClientRoomComponent。"));

            var usings = new List<string>
            {
                "System",
                "System.Collections.Generic",
                "StellarNet.Client.Network",
                "StellarNet.Client.Room",
                "StellarNet.Shared.Identity",
                config.ProtoNamespace,
                "UnityEngine"
            };

            if (config.GenServiceLocatorStub)
                usings.Add("StellarNet.Client.Service");

            sb.Append(CodeTemplateEngine.BuildUsings(usings));

            string body = CodeTemplateEngine.BuildClientRoomHandleBody(config);
            sb.Append(CodeTemplateEngine.WrapInNamespace(config.ClientNamespace, body));

            string relativePath = $"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientHandleClassName}.cs";
            _fileWriteService.Enqueue(relativePath, sb.ToString(), result);
        }

        // ── StableComponentId 校验 ────────────────────────────────

        /// <summary>
        /// 校验 StableComponentId 的格式合法性。
        /// 合法格式：小写字母开头，仅包含小写字母、数字、点、下划线。
        /// 例如：room.turn_system、room.inventory.v2。
        /// 非法格式会在注册表中产生键值冲突或序列化异常，必须在生成前阻断。
        /// </summary>
        private bool ValidateStableComponentId(string stableId, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                result.AddError("[RoomComponentGenerator] StableComponentId 为空，" +
                                "此字段必须全局唯一且跨版本稳定，不可留空。");
                return false;
            }

            if (!StableIdPattern.IsMatch(stableId))
            {
                result.AddError($"[RoomComponentGenerator] StableComponentId \"{stableId}\" 格式非法。" +
                                "合法格式：小写字母开头，仅包含小写字母、数字、点、下划线。" +
                                "例如：room.turn_system");
                return false;
            }

            return true;
        }

        // ── 协议 ID 校验 ──────────────────────────────────────────

        /// <summary>
        /// 校验协议列表中是否存在重复 ID 或非法 ID。
        /// 房间域协议同样不允许使用框架保留号段 0-9999。
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
                    result.AddError($"[RoomComponentGenerator] 协议 ID {p.MessageId}（{p.ClassName}）" +
                                    $"位于框架保留号段 0-9999，请使用 10000 以上的 ID。");
                    valid = false;
                }

                if (!seen.Add(p.MessageId))
                {
                    result.AddError($"[RoomComponentGenerator] 协议 ID {p.MessageId} 重复，" +
                                    $"请检查协议列表中是否存在同 ID 的协议。");
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(p.ClassName))
                {
                    result.AddError($"[RoomComponentGenerator] 协议 ID {p.MessageId} 的 ClassName 为空。");
                    valid = false;
                }
            }

            return valid;
        }
    }
}