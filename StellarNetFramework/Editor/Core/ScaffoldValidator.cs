// ════════════════════════════════════════════════════════════════
// 文件：ScaffoldValidator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/ScaffoldValidator.cs
// 职责：脚手架工具的全局前置校验器。
//       在任何生成操作执行前统一运行，将所有配置层面的问题提前暴露，
//       避免生成到一半因校验失败导致文件集合处于半完成状态。
//       校验结果分为 Error（阻断生成）与 Warning（提示但不阻断）两级。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 脚手架全局校验器，提供对 GlobalModuleConfig 与 RoomComponentConfig 的完整前置校验。
    /// 所有校验方法均为纯函数，无副作用。
    /// </summary>
    public static class ScaffoldValidator
    {
        // 合法的 C# 标识符格式：字母或下划线开头，后跟字母、数字、下划线
        private static readonly Regex CSharpIdentifierPattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        // 合法的 C# 命名空间格式：多段标识符以点分隔
        private static readonly Regex NamespacePattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

        // StableComponentId 合法格式
        private static readonly Regex StableIdPattern =
            new Regex(@"^[a-z][a-z0-9._]*$", RegexOptions.Compiled);

        // ── 全局模块校验 ──────────────────────────────────────────

        /// <summary>
        /// 对 GlobalModuleConfig 执行完整的前置校验。
        /// 返回 true 表示无 Error 级别问题，可以继续生成。
        /// </summary>
        public static bool ValidateGlobalModule(GlobalModuleConfig config, GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[ScaffoldValidator] GlobalModuleConfig 为 null。");
                return false;
            }

            bool valid = true;

            // 模块名称校验
            valid &= ValidateIdentifier(
                config.ModuleName,
                "ModuleName",
                "全局模块",
                result);

            // 命名空间校验
            valid &= ValidateNamespace(config.ClientNamespace, "ClientNamespace", result);
            valid &= ValidateNamespace(config.ServerNamespace, "ServerNamespace", result);
            valid &= ValidateNamespace(config.ProtoNamespace, "ProtoNamespace", result);

            // 输出路径校验
            valid &= ValidateOutputPath(config.ClientOutputPath, "ClientOutputPath", result);
            valid &= ValidateOutputPath(config.ServerOutputPath, "ServerOutputPath", result);
            valid &= ValidateOutputPath(config.ProtoOutputPath, "ProtoOutputPath", result);

            // 至少需要生成一个文件
            bool willGenerateSomething =
                config.GenClientModel || config.GenClientHandle ||
                config.GenServerModel || config.GenServerHandle ||
                config.GenProtoFile;

            if (!willGenerateSomething)
            {
                result.AddError("[ScaffoldValidator] 全局模块：所有生成选项均未勾选，无文件可生成。");
                valid = false;
            }

            // 生成端与生成选项一致性检查
            if (config.Target == GenerateTarget.ClientOnly &&
                (config.GenServerModel || config.GenServerHandle))
            {
                result.AddWarning("[ScaffoldValidator] 全局模块：生成端为「仅客户端」，" +
                                  "但勾选了服务端生成选项，服务端文件将被跳过。");
            }

            if (config.Target == GenerateTarget.ServerOnly &&
                (config.GenClientModel || config.GenClientHandle))
            {
                result.AddWarning("[ScaffoldValidator] 全局模块：生成端为「仅服务端」，" +
                                  "但勾选了客户端生成选项，客户端文件将被跳过。");
            }

            // Handle 依赖 Model 检查
            if (config.GenClientHandle && !config.GenClientModel)
            {
                result.AddWarning("[ScaffoldValidator] 全局模块：勾选了客户端 Handle 但未勾选客户端 Model，" +
                                  "生成的 Handle 将引用不存在的 Model 类，请确认 Model 已手动创建。");
            }

            if (config.GenServerHandle && !config.GenServerModel)
            {
                result.AddWarning("[ScaffoldValidator] 全局模块：勾选了服务端 Handle 但未勾选服务端 Model，" +
                                  "生成的 Handle 将引用不存在的 Model 类，请确认 Model 已手动创建。");
            }

            // 协议列表校验
            if (config.GenProtoFile || config.GenClientHandle || config.GenServerHandle)
            {
                valid &= ValidateProtocols(config.Protocols, "全局模块", result);
            }

            // 文件冲突预检：检查目标路径是否已存在同名文件
            CheckFileConflicts(config, result);

            return valid;
        }

        // ── 房间组件校验 ──────────────────────────────────────────

        /// <summary>
        /// 对 RoomComponentConfig 执行完整的前置校验。
        /// 返回 true 表示无 Error 级别问题，可以继续生成。
        /// </summary>
        public static bool ValidateRoomComponent(RoomComponentConfig config, GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[ScaffoldValidator] RoomComponentConfig 为 null。");
                return false;
            }

            bool valid = true;

            // 组件名称校验
            valid &= ValidateIdentifier(
                config.ComponentName,
                "ComponentName",
                "房间组件",
                result);

            // StableComponentId 格式校验
            if (string.IsNullOrWhiteSpace(config.StableComponentId))
            {
                result.AddError("[ScaffoldValidator] 房间组件：StableComponentId 为空，" +
                                "此字段必须全局唯一且跨版本稳定。");
                valid = false;
            }
            else if (!StableIdPattern.IsMatch(config.StableComponentId))
            {
                result.AddError($"[ScaffoldValidator] 房间组件：StableComponentId \"{config.StableComponentId}\" 格式非法。" +
                                "合法格式：小写字母开头，仅含小写字母、数字、点、下划线。例如：room.turn_system");
                valid = false;
            }

            // 命名空间校验
            valid &= ValidateNamespace(config.ServerNamespace, "ServerNamespace", result);
            valid &= ValidateNamespace(config.ClientNamespace, "ClientNamespace", result);
            valid &= ValidateNamespace(config.ProtoNamespace, "ProtoNamespace", result);

            // 输出路径校验
            valid &= ValidateOutputPath(config.ServerOutputPath, "ServerOutputPath", result);
            valid &= ValidateOutputPath(config.ClientOutputPath, "ClientOutputPath", result);
            valid &= ValidateOutputPath(config.ProtoOutputPath, "ProtoOutputPath", result);

            // 至少需要生成一个文件
            bool willGenerateSomething =
                config.GenServerModel || config.GenServerHandle ||
                config.GenClientModel || config.GenClientHandle ||
                config.GenProtoFile;

            if (!willGenerateSomething)
            {
                result.AddError("[ScaffoldValidator] 房间组件：所有生成选项均未勾选，无文件可生成。");
                valid = false;
            }

            // Handle 依赖 Model 检查
            if (config.GenServerHandle && !config.GenServerModel)
            {
                result.AddWarning("[ScaffoldValidator] 房间组件：勾选了服务端 Handle 但未勾选服务端 Model，" +
                                  "生成的 Handle 将引用不存在的 Model 类，请确认 Model 已手动创建。");
            }

            if (config.GenClientHandle && !config.GenClientModel)
            {
                result.AddWarning("[ScaffoldValidator] 房间组件：勾选了客户端 Handle 但未勾选客户端 Model，" +
                                  "生成的 Handle 将引用不存在的 Model 类，请确认 Model 已手动创建。");
            }

            // 重连快照桩依赖检查
            if (config.GenReconnectSnapshotStub && !config.GenServerHandle)
            {
                result.AddWarning("[ScaffoldValidator] 房间组件：勾选了重连快照补发桩，" +
                                  "但未勾选服务端 Handle，快照方法将无处生成。");
            }

            // 协议列表校验
            if (config.GenProtoFile || config.GenServerHandle || config.GenClientHandle)
            {
                valid &= ValidateProtocols(config.Protocols, "房间组件", result);
            }

            // 文件冲突预检
            CheckRoomFileConflicts(config, result);

            return valid;
        }

        // ── 批量队列校验 ──────────────────────────────────────────

        /// <summary>
        /// 对批量生成队列执行整体校验。
        /// 额外检查队列中是否存在 StableComponentId 重复的房间组件，
        /// 重复的 StableComponentId 会导致注册表键值冲突。
        /// </summary>
        public static bool ValidateBatchQueue(
            List<BatchQueueItem> queue,
            GenerateResult result)
        {
            if (queue == null || queue.Count == 0)
            {
                result.AddError("[ScaffoldValidator] 批量生成队列为空，无任务可执行。");
                return false;
            }

            bool valid = true;
            var stableIdSet = new HashSet<string>();
            var moduleNameSet = new HashSet<string>();

            foreach (var item in queue)
            {
                if (item.Type == BatchQueueItem.ItemType.GlobalModule && item.GlobalConfig != null)
                {
                    // 检查全局模块名重复
                    if (!moduleNameSet.Add(item.GlobalConfig.ModuleName))
                    {
                        result.AddError($"[ScaffoldValidator] 批量队列中存在重复的全局模块名：{item.GlobalConfig.ModuleName}");
                        valid = false;
                    }

                    valid &= ValidateGlobalModule(item.GlobalConfig, result);
                }
                else if (item.Type == BatchQueueItem.ItemType.RoomComponent && item.RoomConfig != null)
                {
                    // 检查 StableComponentId 重复
                    if (!stableIdSet.Add(item.RoomConfig.StableComponentId))
                    {
                        result.AddError($"[ScaffoldValidator] 批量队列中存在重复的 StableComponentId：" +
                                        $"{item.RoomConfig.StableComponentId}");
                        valid = false;
                    }

                    valid &= ValidateRoomComponent(item.RoomConfig, result);
                }
            }

            return valid;
        }

        // ── 私有校验工具 ──────────────────────────────────────────

        /// <summary>
        /// 校验 C# 标识符格式合法性。
        /// </summary>
        private static bool ValidateIdentifier(
            string value,
            string fieldName,
            string context,
            GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError($"[ScaffoldValidator] {context}：{fieldName} 为空。");
                return false;
            }

            if (!CSharpIdentifierPattern.IsMatch(value))
            {
                result.AddError($"[ScaffoldValidator] {context}：{fieldName} \"{value}\" 不是合法的 C# 标识符。" +
                                "应以字母或下划线开头，仅包含字母、数字、下划线。");
                return false;
            }

            // 检查是否以数字开头（正则已覆盖，此处为双重保险）
            if (char.IsDigit(value[0]))
            {
                result.AddError($"[ScaffoldValidator] {context}：{fieldName} \"{value}\" 不能以数字开头。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验命名空间格式合法性。
        /// </summary>
        private static bool ValidateNamespace(
            string ns,
            string fieldName,
            GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} 为空，命名空间不可为空。");
                return false;
            }

            if (!NamespacePattern.IsMatch(ns))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} \"{ns}\" 不是合法的命名空间格式。" +
                                "应为以点分隔的多段 C# 标识符，例如：Game.Client.GlobalModules");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验输出路径格式合法性（相对于 Assets/ 的路径）。
        /// </summary>
        private static bool ValidateOutputPath(
            string path,
            string fieldName,
            GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} 为空，输出路径不可为空。");
                return false;
            }

            if (path.Contains(".."))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} \"{path}\" 包含非法路径穿越字符 '..'。");
                return false;
            }

            if (path.StartsWith("/") || path.StartsWith("\\"))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} \"{path}\" 不应以斜杠开头，" +
                                "请使用相对于 Assets/ 的路径，例如：Game/Client/GlobalModules");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 校验协议列表的完整性与合法性。
        /// </summary>
        private static bool ValidateProtocols(
            List<ProtoDefinition> protos,
            string context,
            GenerateResult result)
        {
            if (protos == null || protos.Count == 0)
            {
                // 协议列表为空不是 Error，只是 Warning，允许生成空协议文件
                result.AddWarning($"[ScaffoldValidator] {context}：协议列表为空，" +
                                  "生成的 Handle 中将只包含 TODO 桩，无实际协议处理方法。");
                return true;
            }

            bool valid = true;
            var seen = new HashSet<int>();

            foreach (var p in protos)
            {
                if (p == null)
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议列表中存在 null 项。");
                    valid = false;
                    continue;
                }

                if (p.MessageId < 10000)
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议 ID {p.MessageId} 位于框架保留号段 0-9999。");
                    valid = false;
                }

                if (!seen.Add(p.MessageId))
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议 ID {p.MessageId} 在列表中重复。");
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(p.ClassName))
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议 ID {p.MessageId} 的 ClassName 为空。");
                    valid = false;
                    continue;
                }

                if (!CSharpIdentifierPattern.IsMatch(p.ClassName))
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议类名 \"{p.ClassName}\" 不是合法的 C# 标识符。");
                    valid = false;
                }

                // 检查协议命名规范：C2S_ 或 S2C_ 前缀
                if (!p.ClassName.StartsWith("C2S_") && !p.ClassName.StartsWith("S2C_"))
                {
                    result.AddWarning($"[ScaffoldValidator] {context}：协议类名 \"{p.ClassName}\" " +
                                      "不符合框架命名规范（应以 C2S_ 或 S2C_ 开头），" +
                                      "这会导致 Handle 中的事件名生成不正确。");
                }
            }

            return valid;
        }

        /// <summary>
        /// 预检全局模块目标路径是否存在文件冲突，存在时输出 Warning。
        /// </summary>
        private static void CheckFileConflicts(GlobalModuleConfig config, GenerateResult result)
        {
            var paths = new List<string>();

            if (config.GenClientModel)
                paths.Add($"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientModelClassName}.cs");

            if (config.GenClientHandle)
                paths.Add($"{config.ClientOutputPath}/{config.ModuleName}/{config.ClientHandleClassName}.cs");

            if (config.GenServerModel)
                paths.Add($"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerModelClassName}.cs");

            if (config.GenServerHandle)
                paths.Add($"{config.ServerOutputPath}/{config.ModuleName}/{config.ServerHandleClassName}.cs");

            if (config.GenProtoFile)
                paths.Add($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs");

            foreach (var path in paths)
            {
                if (FileWriteService.FileExists(path))
                {
                    result.AddWarning($"[ScaffoldValidator] 文件已存在，将被跳过（未开启覆盖）：{path}");
                }
            }
        }

        /// <summary>
        /// 预检房间组件目标路径是否存在文件冲突。
        /// </summary>
        private static void CheckRoomFileConflicts(RoomComponentConfig config, GenerateResult result)
        {
            var paths = new List<string>();

            if (config.GenServerModel)
                paths.Add($"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerModelClassName}.cs");

            if (config.GenServerHandle)
                paths.Add($"{config.ServerOutputPath}/{config.ComponentName}/{config.ServerHandleClassName}.cs");

            if (config.GenClientModel)
                paths.Add($"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientModelClassName}.cs");

            if (config.GenClientHandle)
                paths.Add($"{config.ClientOutputPath}/{config.ComponentName}/{config.ClientHandleClassName}.cs");

            if (config.GenProtoFile)
                paths.Add($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs");

            foreach (var path in paths)
            {
                if (FileWriteService.FileExists(path))
                {
                    result.AddWarning($"[ScaffoldValidator] 文件已存在，将被跳过（未开启覆盖）：{path}");
                }
            }
        }
    }
}