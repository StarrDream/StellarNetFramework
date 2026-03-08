// ════════════════════════════════════════════════════════════════
// 文件：ScaffoldValidator.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/ScaffoldValidator.cs
// 职责：脚手架工具的全局前置校验器。
//       更新：引入 ProtocolScanner，在校验时对比工程中已存在的协议，
//       严防 ID 冲突和类名冲突。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StellarNet.Editor.Scaffold
{
    public static class ScaffoldValidator
    {
        private static readonly Regex CSharpIdentifierPattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private static readonly Regex NamespacePattern =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

        private static readonly Regex StableIdPattern =
            new Regex(@"^[a-z][a-z0-9._]*$", RegexOptions.Compiled);

        // ── 全局模块校验 ──────────────────────────────────────────
        public static bool ValidateGlobalModule(
            GlobalModuleConfig config,
            ProtocolScanner scanner, // 注入扫描器
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[ScaffoldValidator] GlobalModuleConfig 为 null。");
                return false;
            }

            bool valid = true;
            valid &= ValidateIdentifier(config.ModuleName, "ModuleName", "全局模块", result);
            valid &= ValidateNamespace(config.ClientNamespace, "ClientNamespace", result);
            valid &= ValidateNamespace(config.ServerNamespace, "ServerNamespace", result);
            valid &= ValidateNamespace(config.ProtoNamespace, "ProtoNamespace", result);
            valid &= ValidateOutputPath(config.ClientOutputPath, "ClientOutputPath", result);
            valid &= ValidateOutputPath(config.ServerOutputPath, "ServerOutputPath", result);
            valid &= ValidateOutputPath(config.ProtoOutputPath, "ProtoOutputPath", result);

            bool willGenerateSomething = config.GenClientModel || config.GenClientHandle ||
                                         config.GenServerModel || config.GenServerHandle ||
                                         config.GenProtoFile;

            if (!willGenerateSomething)
            {
                result.AddError("[ScaffoldValidator] 全局模块：所有生成选项均未勾选。");
                valid = false;
            }

            // 协议列表校验（含全局防重）
            if (config.GenProtoFile || config.GenClientHandle || config.GenServerHandle)
            {
                valid &= ValidateProtocols(config.Protocols, scanner, "全局模块", result);
            }

            CheckFileConflicts(config, result);
            return valid;
        }

        // ── 房间组件校验 ──────────────────────────────────────────
        public static bool ValidateRoomComponent(
            RoomComponentConfig config,
            ProtocolScanner scanner, // 注入扫描器
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[ScaffoldValidator] RoomComponentConfig 为 null。");
                return false;
            }

            bool valid = true;
            valid &= ValidateIdentifier(config.ComponentName, "ComponentName", "房间组件", result);

            if (string.IsNullOrWhiteSpace(config.StableComponentId))
            {
                result.AddError("[ScaffoldValidator] 房间组件：StableComponentId 为空。");
                valid = false;
            }
            else if (!StableIdPattern.IsMatch(config.StableComponentId))
            {
                result.AddError($"[ScaffoldValidator] 房间组件：StableComponentId \"{config.StableComponentId}\" 格式非法。");
                valid = false;
            }

            valid &= ValidateNamespace(config.ServerNamespace, "ServerNamespace", result);
            valid &= ValidateNamespace(config.ClientNamespace, "ClientNamespace", result);
            valid &= ValidateNamespace(config.ProtoNamespace, "ProtoNamespace", result);
            valid &= ValidateOutputPath(config.ServerOutputPath, "ServerOutputPath", result);
            valid &= ValidateOutputPath(config.ClientOutputPath, "ClientOutputPath", result);
            valid &= ValidateOutputPath(config.ProtoOutputPath, "ProtoOutputPath", result);

            bool willGenerateSomething = config.GenServerModel || config.GenServerHandle ||
                                         config.GenClientModel || config.GenClientHandle ||
                                         config.GenProtoFile;

            if (!willGenerateSomething)
            {
                result.AddError("[ScaffoldValidator] 房间组件：所有生成选项均未勾选。");
                valid = false;
            }

            // 协议列表校验（含全局防重）
            if (config.GenProtoFile || config.GenServerHandle || config.GenClientHandle)
            {
                valid &= ValidateProtocols(config.Protocols, scanner, "房间组件", result);
            }

            CheckRoomFileConflicts(config, result);
            return valid;
        }

        // ── 批量队列校验 ──────────────────────────────────────────
        public static bool ValidateBatchQueue(
            List<BatchQueueItem> queue,
            ProtocolScanner scanner,
            GenerateResult result)
        {
            if (queue == null || queue.Count == 0)
            {
                result.AddError("[ScaffoldValidator] 批量生成队列为空。");
                return false;
            }

            bool valid = true;
            var stableIdSet = new HashSet<string>();
            var moduleNameSet = new HashSet<string>();

            // 批量生成时，还需要检查队列内部的协议 ID 冲突
            var batchInternalIds = new HashSet<int>();
            var batchInternalNames = new HashSet<string>();

            foreach (var item in queue)
            {
                List<ProtoDefinition> protos = null;
                string context = "";

                if (item.Type == BatchQueueItem.ItemType.GlobalModule && item.GlobalConfig != null)
                {
                    if (!moduleNameSet.Add(item.GlobalConfig.ModuleName))
                    {
                        result.AddError($"[ScaffoldValidator] 批量队列重复模块名：{item.GlobalConfig.ModuleName}");
                        valid = false;
                    }

                    valid &= ValidateGlobalModule(item.GlobalConfig, scanner, result);
                    protos = item.GlobalConfig.Protocols;
                    context = $"全局模块 {item.GlobalConfig.ModuleName}";
                }
                else if (item.Type == BatchQueueItem.ItemType.RoomComponent && item.RoomConfig != null)
                {
                    if (!stableIdSet.Add(item.RoomConfig.StableComponentId))
                    {
                        result.AddError(
                            $"[ScaffoldValidator] 批量队列重复 StableComponentId：{item.RoomConfig.StableComponentId}");
                        valid = false;
                    }

                    valid &= ValidateRoomComponent(item.RoomConfig, scanner, result);
                    protos = item.RoomConfig.Protocols;
                    context = $"房间组件 {item.RoomConfig.ComponentName}";
                }

                // 队列内部防重检查
                if (protos != null)
                {
                    foreach (var p in protos)
                    {
                        if (!batchInternalIds.Add(p.MessageId))
                        {
                            result.AddError($"[ScaffoldValidator] 批量队列内部存在重复 ID {p.MessageId}（{context}）。");
                            valid = false;
                        }

                        if (!batchInternalNames.Add(p.ClassName))
                        {
                            result.AddError($"[ScaffoldValidator] 批量队列内部存在重复类名 {p.ClassName}（{context}）。");
                            valid = false;
                        }
                    }
                }
            }

            return valid;
        }

        // ── 协议列表校验（核心更新） ──────────────────────────────
        private static bool ValidateProtocols(
            List<ProtoDefinition> protos,
            ProtocolScanner scanner,
            string context,
            GenerateResult result)
        {
            if (protos == null || protos.Count == 0)
            {
                result.AddWarning($"[ScaffoldValidator] {context}：协议列表为空。");
                return true;
            }

            bool valid = true;
            var localSeenIds = new HashSet<int>();
            var localSeenNames = new HashSet<string>();

            foreach (var p in protos)
            {
                if (p == null) continue;

                // 1. 基础格式校验
                if (p.MessageId < 10000)
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议 ID {p.MessageId} 位于保留号段 0-9999。");
                    valid = false;
                }

                if (string.IsNullOrWhiteSpace(p.ClassName))
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议 ID {p.MessageId} 类名为空。");
                    valid = false;
                    continue;
                }

                if (!CSharpIdentifierPattern.IsMatch(p.ClassName))
                {
                    result.AddError($"[ScaffoldValidator] {context}：协议类名 \"{p.ClassName}\" 非法。");
                    valid = false;
                }

                // 2. 本地列表重复校验
                if (!localSeenIds.Add(p.MessageId))
                {
                    result.AddError($"[ScaffoldValidator] {context}：当前列表中 ID {p.MessageId} 重复。");
                    valid = false;
                }

                if (!localSeenNames.Add(p.ClassName))
                {
                    result.AddError($"[ScaffoldValidator] {context}：当前列表中类名 {p.ClassName} 重复。");
                    valid = false;
                }

                // 3. 全局工程重复校验（使用 Scanner）
                if (scanner != null)
                {
                    if (scanner.UsedIds.Contains(p.MessageId))
                    {
                        string existingName = scanner.IdToNameMap.TryGetValue(p.MessageId, out var n) ? n : "未知";
                        result.AddError($"[ScaffoldValidator] {context}：ID {p.MessageId} 已被现有代码占用（{existingName}）。");
                        valid = false;
                    }

                    if (scanner.UsedClassNames.Contains(p.ClassName))
                    {
                        result.AddError($"[ScaffoldValidator] {context}：类名 {p.ClassName} 已被现有代码占用。");
                        valid = false;
                    }
                }
            }

            return valid;
        }

        // ── 私有校验工具（保持不变） ──────────────────────────────
        private static bool ValidateIdentifier(string value, string fieldName, string context, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError($"[ScaffoldValidator] {context}：{fieldName} 为空。");
                return false;
            }

            if (!CSharpIdentifierPattern.IsMatch(value))
            {
                result.AddError($"[ScaffoldValidator] {context}：{fieldName} \"{value}\" 格式非法。");
                return false;
            }

            return true;
        }

        private static bool ValidateNamespace(string ns, string fieldName, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} 为空。");
                return false;
            }

            if (!NamespacePattern.IsMatch(ns))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} \"{ns}\" 格式非法。");
                return false;
            }

            return true;
        }

        private static bool ValidateOutputPath(string path, string fieldName, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} 为空。");
                return false;
            }

            if (path.Contains("..") || path.StartsWith("/") || path.StartsWith("\\"))
            {
                result.AddError($"[ScaffoldValidator] {fieldName} 路径非法。");
                return false;
            }

            return true;
        }

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
            if (config.GenProtoFile) paths.Add($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs");

            foreach (var path in paths)
            {
                if (FileWriteService.FileExists(path))
                    result.AddWarning($"[ScaffoldValidator] 文件已存在（未开启覆盖）：{path}");
            }
        }

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
            if (config.GenProtoFile) paths.Add($"{config.ProtoOutputPath}/{config.ProtoFileName}.cs");

            foreach (var path in paths)
            {
                if (FileWriteService.FileExists(path))
                    result.AddWarning($"[ScaffoldValidator] 文件已存在（未开启覆盖）：{path}");
            }
        }
    }
}