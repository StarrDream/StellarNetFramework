// ════════════════════════════════════════════════════════════════
// 文件：FileWriteService.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/FileWriteService.cs
// 职责：脚手架工具唯一的文件 IO 层。
//       所有磁盘写入操作必须经过此服务，不允许 Generator 直接调用
//       System.IO.File，保证 IO 行为可统一审计、路径可统一校验。
//       写入完成后统一触发 AssetDatabase.Refresh，避免多次刷新。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 文件写入服务，封装所有磁盘 IO 操作。
    /// 提供写入前的路径合法性校验、目录自动创建、文件覆盖保护三道防线。
    /// 所有写入结果记录到 GenerateResult，由调用方决定如何展示。
    /// </summary>
    public sealed class FileWriteService
    {
        // ── 路径常量 ──────────────────────────────────────────────

        /// <summary>
        /// Assets 根目录的绝对路径，所有相对路径基于此拼接。
        /// </summary>
        private static readonly string AssetsRoot =
            Application.dataPath;

        // ── 写入队列 ──────────────────────────────────────────────

        /// <summary>
        /// 本次会话的待写入文件列表，调用 FlushAll 后统一写入并刷新 AssetDatabase。
        /// 使用队列模式而非逐文件写入，目的是将 AssetDatabase.Refresh 合并为一次调用，
        /// 避免在批量生成时因多次刷新导致编辑器卡顿。
        /// </summary>
        private readonly List<PendingFile> _pendingFiles = new List<PendingFile>();

        private sealed class PendingFile
        {
            public string AbsolutePath;
            public string Content;
            public bool IsOverwrite;
        }

        // ── 入队 ──────────────────────────────────────────────────

        /// <summary>
        /// 将一个文件写入请求加入队列。
        /// 此方法只做路径合法性校验，不执行实际 IO，实际写入在 FlushAll 中统一执行。
        /// </summary>
        /// <param name="relativeToAssets">相对于 Assets/ 的路径，例如 Game/Client/GlobalModules/MyFeature/ClientMyFeatureHandle.cs</param>
        /// <param name="content">文件完整内容字符串。</param>
        /// <param name="allowOverwrite">是否允许覆盖已存在的文件，默认 false 防止误覆盖。</param>
        /// <param name="result">写入结果记录，校验失败时写入错误信息。</param>
        public void Enqueue(
            string relativeToAssets,
            string content,
            GenerateResult result,
            bool allowOverwrite = false)
        {
            if (string.IsNullOrWhiteSpace(relativeToAssets))
            {
                result.AddError("[FileWriteService] 入队失败：relativeToAssets 为空。");
                return;
            }

            if (string.IsNullOrEmpty(content))
            {
                result.AddError($"[FileWriteService] 入队失败：文件内容为空，路径：{relativeToAssets}");
                return;
            }

            // 路径安全检查：防止路径穿越攻击（../../ 等）
            if (relativeToAssets.Contains(".."))
            {
                result.AddError($"[FileWriteService] 入队失败：路径包含非法字符 '..'，路径：{relativeToAssets}");
                return;
            }

            string absPath = Path.Combine(AssetsRoot, relativeToAssets).Replace('\\', '/');
            bool exists = File.Exists(absPath);

            if (exists && !allowOverwrite)
            {
                result.AddWarning($"[FileWriteService] 跳过：文件已存在且未开启覆盖，路径：{relativeToAssets}");
                return;
            }

            _pendingFiles.Add(new PendingFile
            {
                AbsolutePath = absPath,
                Content = content,
                IsOverwrite = exists
            });
        }

        // ── 执行写入 ──────────────────────────────────────────────

        /// <summary>
        /// 将队列中所有文件统一写入磁盘，完成后触发一次 AssetDatabase.Refresh。
        /// 写入失败的文件记录到 result.Errors，不中断其他文件的写入。
        /// </summary>
        public void FlushAll(GenerateResult result)
        {
            if (_pendingFiles.Count == 0)
            {
                result.AddWarning("[FileWriteService] FlushAll 调用时队列为空，无文件写入。");
                return;
            }

            foreach (var file in _pendingFiles)
            {
                WriteFile(file, result);
            }

            _pendingFiles.Clear();

            // 统一刷新一次，避免多次刷新导致编辑器卡顿
            AssetDatabase.Refresh();
        }

        // ── 私有写入 ──────────────────────────────────────────────

        /// <summary>
        /// 执行单个文件的实际磁盘写入。
        /// 目录不存在时自动创建，写入失败记录错误但不抛出异常，防止中断批量流程。
        /// </summary>
        private void WriteFile(PendingFile file, GenerateResult result)
        {
            string dir = Path.GetDirectoryName(file.AbsolutePath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 使用 StreamWriter 而非 File.WriteAllText，
            // 明确指定 UTF-8 with BOM 编码，与 Unity 默认脚本编码保持一致
            using (var writer = new StreamWriter(file.AbsolutePath, false, new System.Text.UTF8Encoding(true)))
            {
                writer.Write(file.Content);
            }

            if (file.IsOverwrite)
                result.AddModified(file.AbsolutePath);
            else
                result.AddWritten(file.AbsolutePath);
        }

        // ── 工具方法 ──────────────────────────────────────────────

        /// <summary>
        /// 检查指定路径的文件是否已存在，供 Generator 在入队前做预检。
        /// </summary>
        public static bool FileExists(string relativeToAssets)
        {
            if (string.IsNullOrWhiteSpace(relativeToAssets))
                return false;

            string absPath = Path.Combine(AssetsRoot, relativeToAssets).Replace('\\', '/');
            return File.Exists(absPath);
        }

        /// <summary>
        /// 将相对路径转换为绝对路径，供外部预览使用。
        /// </summary>
        public static string ToAbsolutePath(string relativeToAssets)
        {
            return Path.Combine(AssetsRoot, relativeToAssets).Replace('\\', '/');
        }

        /// <summary>
        /// 清空当前队列，用于用户取消操作时的状态重置。
        /// </summary>
        public void Clear()
        {
            _pendingFiles.Clear();
        }

        /// <summary>
        /// 返回当前队列中待写入的文件数量，供 UI 层展示"将生成 N 个文件"。
        /// </summary>
        public int PendingCount => _pendingFiles.Count;
    }
}