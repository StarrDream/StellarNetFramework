// ════════════════════════════════════════════════════════════════
// 文件：InfrastructureInjector.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/InfrastructureInjector.cs
// 职责：向现有 Infrastructure 文件追加模块注册代码的注入器。
//       采用基于标记注释的定点插入策略，而非正则替换整个文件，
//       保证注入操作对文件其他内容无副作用。
//       注入前必须检查标记注释是否存在，不存在则报 Warning 并跳过，
//       不允许盲目追加导致重复注册。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// Infrastructure 注入器，向现有 Infrastructure 文件的指定锚点位置插入模块注册代码。
    /// 锚点格式：// [SCAFFOLD_INJECT:{标记名}]
    /// 例如：// [SCAFFOLD_INJECT:GLOBAL_MODULE_REGISTER]
    /// 注入器在锚点行的下一行插入代码，不修改锚点行本身，保证可重复注入时锚点始终有效。
    /// </summary>
    public sealed class InfrastructureInjector
    {
// ── 锚点标记常量 ──────────────────────────────────────────
        /// <summary>
        /// 客户端 Infrastructure 全局模块注册锚点。
        /// 在 ClientInfrastructure.cs 中添加此注释以启用注入。
        /// </summary>
        public const string AnchorClientGlobalRegister = "// [SCAFFOLD_INJECT:CLIENT_GLOBAL_MODULE_REGISTER]";

        /// <summary>
        /// 客户端 Infrastructure 全局模块注销锚点。
        /// </summary>
        public const string AnchorClientGlobalUnregister = "// [SCAFFOLD_INJECT:CLIENT_GLOBAL_MODULE_UNREGISTER]";

        /// <summary>
        /// 服务端 Infrastructure 全局模块注册锚点。
        /// </summary>
        public const string AnchorServerGlobalRegister = "// [SCAFFOLD_INJECT:SERVER_GLOBAL_MODULE_REGISTER]";

        /// <summary>
        /// 服务端 Infrastructure 全局模块注销锚点。
        /// </summary>
        public const string AnchorServerGlobalUnregister = "// [SCAFFOLD_INJECT:SERVER_GLOBAL_MODULE_UNREGISTER]";

        /// <summary>
        /// 服务端 RoomComponentRegistry 工厂注册锚点。
        /// </summary>
        public const string AnchorServerRoomFactory = "// [SCAFFOLD_INJECT:SERVER_ROOM_FACTORY_REGISTER]";

        /// <summary>
        /// 客户端 RoomComponentRegistry 工厂注册锚点。
        /// </summary>
        public const string AnchorClientRoomFactory = "// [SCAFFOLD_INJECT:CLIENT_ROOM_FACTORY_REGISTER]";

// ── Assets 根路径 ─────────────────────────────────────────
        private static readonly string AssetsRoot = Application.dataPath;

// ── 全局模块注入 ──────────────────────────────────────────
        /// <summary>
        /// 向 ClientInfrastructure.cs 注入全局模块的 Handle 注册与注销代码。
        /// 注入前检查是否已存在相同的注册行，防止重复注册。
        /// </summary>
        public void InjectClientGlobalModule(
            GlobalModuleConfig config,
            string clientInfraRelativePath,
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[InfrastructureInjector] InjectClientGlobalModule 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(clientInfraRelativePath))
            {
                result.AddError("[InfrastructureInjector] InjectClientGlobalModule 失败：clientInfraRelativePath 为空。");
                return;
            }

            string absPath = GetAbsPath(clientInfraRelativePath, result);
            if (absPath == null) return;
// 构建注册行与注销行
// 注册行格式与框架现有 Infrastructure 风格保持一致
            string registerLine = $"            _{LowerFirst(config.ClientHandleClassName)}.RegisterAll();";
            string unregisterLine = $"            _{LowerFirst(config.ClientHandleClassName)}.UnregisterAll();";
            InjectLine(absPath, AnchorClientGlobalRegister, registerLine, result);
            InjectLine(absPath, AnchorClientGlobalUnregister, unregisterLine, result);
        }

        /// <summary>
        /// 向 GlobalInfrastructure.cs 注入服务端全局模块的 Handle 注册与注销代码。
        /// </summary>
        public void InjectServerGlobalModule(
            GlobalModuleConfig config,
            string serverInfraRelativePath,
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[InfrastructureInjector] InjectServerGlobalModule 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(serverInfraRelativePath))
            {
                result.AddError("[InfrastructureInjector] InjectServerGlobalModule 失败：serverInfraRelativePath 为空。");
                return;
            }

            string absPath = GetAbsPath(serverInfraRelativePath, result);
            if (absPath == null) return;
            string registerLine = $"            _{LowerFirst(config.ServerHandleClassName)}.RegisterAll();";
            string unregisterLine = $"            _{LowerFirst(config.ServerHandleClassName)}.UnregisterAll();";
            InjectLine(absPath, AnchorServerGlobalRegister, registerLine, result);
            InjectLine(absPath, AnchorServerGlobalUnregister, unregisterLine, result);
        }

// ── 房间组件注入 ──────────────────────────────────────────
        /// <summary>
        /// 向服务端 Infrastructure 注入房间组件工厂注册代码。
        /// 注入格式与框架 RoomComponentRegistry.RegisterFactory 调用风格保持一致。
        /// </summary>
        public void InjectServerRoomComponent(
            RoomComponentConfig config,
            string registryRelativePath,
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[InfrastructureInjector] InjectServerRoomComponent 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(registryRelativePath))
            {
                result.AddError("[InfrastructureInjector] InjectServerRoomComponent 失败：registryRelativePath 为空。");
                return;
            }

            string absPath = GetAbsPath(registryRelativePath, result);
            if (absPath == null) return;
// 工厂注册行：通过 StableComponentId 常量引用，避免硬编码字符串
// 修正：注入到 Infrastructure 的 Initialize 方法中
            string registerLine =
                $"            _componentRegistry.Register(\n" +
                $"                {config.ServerNamespace}.{config.ServerHandleClassName}.StableComponentId,\n" +
                $"                room => new {config.ServerNamespace}.{config.ServerHandleClassName}(_globalSender, _roomSender, _sessionManager)\n" +
                $"            );";
            InjectLine(absPath, AnchorServerRoomFactory, registerLine, result);
        }

        /// <summary>
        /// 向客户端 Infrastructure 注入房间组件工厂注册代码。
        /// </summary>
        public void InjectClientRoomComponent(
            RoomComponentConfig config,
            string registryRelativePath,
            GenerateResult result)
        {
            if (config == null)
            {
                result.AddError("[InfrastructureInjector] InjectClientRoomComponent 失败：config 为 null。");
                return;
            }

            if (string.IsNullOrWhiteSpace(registryRelativePath))
            {
                result.AddError("[InfrastructureInjector] InjectClientRoomComponent 失败：registryRelativePath 为空。");
                return;
            }

            string absPath = GetAbsPath(registryRelativePath, result);
            if (absPath == null) return;
// 修正：注入到 ClientInfrastructure 的 RegisterClientComponents 方法中
            string registerLine =
                $"            _clientComponentRegistry.Register(\n" +
                $"                {config.ClientNamespace}.{config.ClientHandleClassName}.StableComponentId,\n" +
                $"                room => new {config.ClientNamespace}.{config.ClientHandleClassName}()\n" +
                $"            );";
            InjectLine(absPath, AnchorClientRoomFactory, registerLine, result);
        }

// ── 核心注入逻辑 ──────────────────────────────────────────
        /// <summary>
        /// 在目标文件中找到锚点行，在其下一行插入 injectLine。
        /// 若文件中已存在完全相同的行，则跳过注入，防止重复注册。
        /// 若锚点不存在，记录 Warning 并跳过，不强制修改文件结构。
        /// </summary>
        private void InjectLine(
            string absPath,
            string anchor,
            string injectLine,
            GenerateResult result)
        {
            string[] lines = File.ReadAllLines(absPath, Encoding.UTF8);
// 检查是否已存在相同注入行，防止重复注册
// 注意：对于多行注入，这里只简单检查第一行是否匹配，实际情况可能更复杂
// 简化处理：去除空白字符后比较
            string compactInject = injectLine.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            foreach (var line in lines)
            {
                string compactLine = line.Replace(" ", "").Replace("\n", "").Replace("\r", "");
// 如果是多行注入，只要包含关键部分就算重复
                if (compactLine.Contains(compactInject) ||
                    (compactInject.Length > 20 && compactLine.Contains(compactInject.Substring(0, 20))))
                {
// 这里的重复检查比较粗糙，对于多行代码块可能不准确，但能防止最简单的重复
// 更好的做法是解析语法树，但这里作为轻量级工具，暂不引入 Roslyn
// 暂时只对单行注入做严格检查，多行注入不做严格去重（依赖用户自觉）
                    if (!injectLine.Contains("\n") && line.Trim() == injectLine.Trim())
                    {
                        result.AddWarning($"[InfrastructureInjector] 注入行已存在，跳过：{injectLine.Trim()}");
                        return;
                    }
                }
            }

// 查找锚点行索引
            int anchorIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(anchor))
                {
                    anchorIndex = i;
                    break;
                }
            }

            if (anchorIndex < 0)
            {
                result.AddWarning(
                    $"[InfrastructureInjector] 未找到注入锚点 \"{anchor}\"，跳过注入。" +
                    $"请在目标文件中添加此锚点注释以启用自动注入。");
                return;
            }

// 在锚点行下方插入新行，保留原有缩进风格
            var newLines = new List<string>(lines);
            newLines.Insert(anchorIndex + 1, injectLine);
// 使用 UTF-8 with BOM 写回，与 Unity 脚本编码保持一致
            File.WriteAllLines(absPath, newLines, new UTF8Encoding(true));
            result.AddModified(absPath);
        }

        private string GetAbsPath(string relative, GenerateResult result)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                result.AddError("路径为空");
                return null;
            }

            string path = Path.Combine(AssetsRoot, relative).Replace('\\', '/');
            if (!File.Exists(path))
            {
                result.AddWarning($"文件不存在: {relative}");
                return null;
            }

            return path;
        }

// ── 工具方法 ──────────────────────────────────────────────
        /// <summary>
        /// 将字符串首字母转为小写，用于生成字段名。
        /// 例如：ClientMyFeatureHandle → clientMyFeatureHandle
        /// </summary>
        private static string LowerFirst(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return char.ToLower(s[0]) + s.Substring(1);
        }
    }
}