using System.IO;
using StellarNet.Server.Network;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.ReplayModule
{
    /// <summary>
    /// 回放模块 Handle，处理回放列表查询、元信息查询与分块文件传输。
    /// 回放文件分块传输：每次响应一个分块请求，客户端按序请求所有分块后自行拼装并校验 MD5。
    /// ContentMd5 在元信息响应阶段下发，不在每个分块中重复携带，减少传输体积。
    /// 分块大小由 ChunkSizeBytes 常量控制，框架默认 64KB。
    /// 服务端不主动推送分块，只响应客户端的分块请求，防止推送速率超出客户端处理能力。
    /// 文件 IO 操作允许使用 try-catch，属于不可控底层 IO，符合框架 try-catch 使用规范。
    /// </summary>
    public sealed class ReplayHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly ReplayModel _model;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly GlobalMessageRegistrar _registrar;

        // 回放文件分块大小，64KB
        private const int ChunkSizeBytes = 64 * 1024;

        public ReplayHandle(
            SessionManager sessionManager,
            ReplayModel model,
            ServerGlobalMessageSender globalSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[ReplayHandle] 构造失败：sessionManager 为 null。");
                return;
            }

            if (model == null)
            {
                Debug.LogError("[ReplayHandle] 构造失败：model 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[ReplayHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ReplayHandle] 构造失败：registrar 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _model = model;
            _globalSender = globalSender;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<C2S_GetReplayList>(OnC2S_GetReplayList)
                .Register<C2S_GetReplayMeta>(OnC2S_GetReplayMeta)
                .Register<C2S_RequestReplayChunk>(OnC2S_RequestReplayChunk)
                .Register<C2S_RequestEnterReplay>(OnC2S_RequestEnterReplay);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<C2S_GetReplayList>()
                .Unregister<C2S_GetReplayMeta>()
                .Unregister<C2S_RequestReplayChunk>()
                .Unregister<C2S_RequestEnterReplay>();
        }

        private void OnC2S_GetReplayList(ConnectionId connectionId, C2S_GetReplayList message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[ReplayHandle] 获取回放列表失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            int pageSize = message.PageSize > 0 ? message.PageSize : 10;
            var records = _model.GetReplayList(message.PageIndex, pageSize);
            var briefs = new ReplayBriefInfo[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                briefs[i] = new ReplayBriefInfo
                {
                    ReplayId = records[i].ReplayId,
                    RecordStartUnixMs = records[i].RecordStartUnixMs,
                    TotalTicks = records[i].TotalTicks,
                    FileSizeBytes = records[i].FileSizeBytes
                };
            }

            var result = new S2C_ReplayListResult
            {
                Replays = briefs,
                TotalCount = _model.TotalCount
            };
            _globalSender.SendToSession(session.SessionId, result);
        }

        private void OnC2S_GetReplayMeta(ConnectionId connectionId, C2S_GetReplayMeta message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[ReplayHandle] 获取回放元信息失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.ReplayId))
            {
                Debug.LogError($"[ReplayHandle] 获取回放元信息失败：ReplayId 为空，SessionId={session.SessionId}。");
                return;
            }

            var meta = _model.GetMeta(message.ReplayId);
            if (meta == null)
            {
                var failResult = new S2C_ReplayMetaResult
                {
                    Success = false,
                    ReplayId = message.ReplayId,
                    FailReason = "回放不存在"
                };
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            long fileSize = meta.FileSizeBytes;
            int totalChunks = (int)System.Math.Ceiling((double)fileSize / ChunkSizeBytes);
            if (totalChunks <= 0)
            {
                totalChunks = 1;
            }

            // ContentMd5 在元信息响应阶段一并下发，客户端缓存后在拼装完成时做完整性校验
            // 不在每个分块中重复携带，减少传输体积
            var result = new S2C_ReplayMetaResult
            {
                Success = true,
                ReplayId = meta.ReplayId,
                FrameworkVersion = meta.FrameworkVersion,
                ProtocolVersion = meta.ProtocolVersion,
                FileSizeBytes = fileSize,
                TotalChunks = totalChunks,
                ContentMd5 = meta.ContentMd5 ?? string.Empty,
                FailReason = string.Empty
            };
            _globalSender.SendToSession(session.SessionId, result);
        }

        private void OnC2S_RequestReplayChunk(ConnectionId connectionId, C2S_RequestReplayChunk message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[ReplayHandle] 回放分块请求失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.ReplayId))
            {
                Debug.LogError($"[ReplayHandle] 回放分块请求失败：ReplayId 为空，SessionId={session.SessionId}。");
                return;
            }

            var meta = _model.GetMeta(message.ReplayId);
            if (meta == null)
            {
                Debug.LogError(
                    $"[ReplayHandle] 回放分块请求失败：ReplayId={message.ReplayId} 元信息不存在，SessionId={session.SessionId}。");
                return;
            }

            if (!File.Exists(meta.FilePath))
            {
                Debug.LogError($"[ReplayHandle] 回放分块请求失败：文件不存在，路径={meta.FilePath}，" +
                               $"ReplayId={message.ReplayId}，SessionId={session.SessionId}。");
                return;
            }

            long fileSize = meta.FileSizeBytes;
            int totalChunks = (int)System.Math.Ceiling((double)fileSize / ChunkSizeBytes);
            if (totalChunks <= 0)
            {
                totalChunks = 1;
            }

            if (message.ChunkIndex < 0 || message.ChunkIndex >= totalChunks)
            {
                Debug.LogError($"[ReplayHandle] 回放分块请求失败：ChunkIndex={message.ChunkIndex} 越界，" +
                               $"TotalChunks={totalChunks}，ReplayId={message.ReplayId}，SessionId={session.SessionId}。");
                return;
            }

            // 文件 IO 属于不可控底层操作，允许使用 try-catch，符合框架规范
            byte[] chunkData;
            try
            {
                long offset = (long)message.ChunkIndex * ChunkSizeBytes;
                int readLength = (int)System.Math.Min(ChunkSizeBytes, fileSize - offset);
                chunkData = new byte[readLength];
                using (var fs = new FileStream(meta.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    int bytesRead = fs.Read(chunkData, 0, readLength);
                    if (bytesRead != readLength)
                    {
                        Debug.LogError($"[ReplayHandle] 回放分块读取字节数不符：期望={readLength}，实际={bytesRead}，" +
                                       $"ChunkIndex={message.ChunkIndex}，ReplayId={message.ReplayId}，SessionId={session.SessionId}。");
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ReplayHandle] 回放分块文件读取异常：{ex.Message}，" +
                               $"ChunkIndex={message.ChunkIndex}，ReplayId={message.ReplayId}，SessionId={session.SessionId}。");
                return;
            }

            var chunkMsg = new S2C_ReplayChunk
            {
                ReplayId = message.ReplayId,
                ChunkIndex = message.ChunkIndex,
                TotalChunks = totalChunks,
                PayloadLength = chunkData.Length,
                TotalLength = fileSize,
                ChunkData = chunkData
            };
            _globalSender.SendToSession(session.SessionId, chunkMsg);
            Debug.Log($"[ReplayHandle] 回放分块发送成功，ReplayId={message.ReplayId}，" +
                      $"ChunkIndex={message.ChunkIndex}/{totalChunks - 1}，SessionId={session.SessionId}。");
        }

        private void OnC2S_RequestEnterReplay(ConnectionId connectionId, C2S_RequestEnterReplay message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[ReplayHandle] 进入回放校验失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.ReplayId))
            {
                Debug.LogError($"[ReplayHandle] 进入回放校验失败：ReplayId 为空，SessionId={session.SessionId}。");
                return;
            }

            var meta = _model.GetMeta(message.ReplayId);
            if (meta == null)
            {
                var failResult = new S2C_EnterReplayResult
                {
                    Success = false,
                    ReplayId = message.ReplayId,
                    FailReason = "回放不存在"
                };
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            if (!File.Exists(meta.FilePath))
            {
                var failResult = new S2C_EnterReplayResult
                {
                    Success = false,
                    ReplayId = message.ReplayId,
                    FailReason = "回放文件已失效"
                };
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            var result = new S2C_EnterReplayResult
            {
                Success = true,
                ReplayId = message.ReplayId,
                FailReason = string.Empty
            };
            _globalSender.SendToSession(session.SessionId, result);
        }
    }
}