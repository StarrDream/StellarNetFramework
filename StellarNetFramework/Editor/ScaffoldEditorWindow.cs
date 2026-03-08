// ════════════════════════════════════════════════════════════════
// 文件：ScaffoldEditorWindow.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/ScaffoldEditorWindow.cs
// 职责：脚手架工具 EditorWindow 主体，负责所有 UI 绘制与用户交互。
//       UI 层不承载任何生成逻辑，所有生成操作委托给对应 Generator。
//       窗口状态通过 EditorPrefs 持久化，关闭后重新打开保留上次配置。
//       侧边栏背景在 OnGUI 顶层用绝对坐标绘制，不参与 Layout 流。
//       集成 ProtocolScanner 实现开发期协议 ID 查重与自动建议。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    public sealed class ScaffoldEditorWindow : EditorWindow
    {
        // ── 常量 ──────────────────────────────────────────────────
        private const float SidebarWidth = 190f;
        private const float NavItemHeight = 30f;
        private const float LabelWidth = 148f;

        private const float ActionBarHeight = 44f;

        // 路径选择按钮宽度
        private const float FolderBtnWidth = 26f;

        // ── 菜单入口 ──────────────────────────────────────────────
        [MenuItem("StellarNet/Scaffold Tool")]
        private static void OpenWindow()
        {
            var w = GetWindow<ScaffoldEditorWindow>("StellarNet 脚手架");
            w.minSize = new Vector2(860f, 560f);
            w.Show();
        }

        // ── 面板枚举 ──────────────────────────────────────────────
        private enum Panel
        {
            GlobalModule,
            RoomComponent,
            ProtoOnly,
            Batch,
            Log
        }

        // ── 运行时状态 ────────────────────────────────────────────
        private Panel _currentPanel = Panel.GlobalModule;
        private GlobalModuleConfig _globalConfig = new GlobalModuleConfig();
        private RoomComponentConfig _roomConfig = new RoomComponentConfig();

        private string _protoOnlyFileName = "MyMessages";
        private string _protoOnlyOutputPath = "Game/Shared/Protocol";
        private string _protoOnlyNamespace = "Game.Shared.Protocol";
        private string _protoOnlyModuleName = "My";
        private string _protoOnlyDomain = "Global（全局域）";
        private readonly List<ProtoDefinition> _protoOnlyList = new List<ProtoDefinition>();

        private readonly List<BatchQueueItem> _batchQueue = new List<BatchQueueItem>();
        private readonly List<string> _logLines = new List<string>();

        private Vector2 _logScroll;
        private Vector2 _globalScroll;
        private Vector2 _roomScroll;
        private Vector2 _protoScroll;
        private Vector2 _batchScroll;
        private Vector2 _globalProtoScroll;
        private Vector2 _roomProtoScroll;
        private Vector2 _protoOnlyProtoScroll;

        private FileWriteService _fileWriteService;
        private GlobalModuleGenerator _globalGenerator;
        private RoomComponentGenerator _roomGenerator;
        private ProtoOnlyGenerator _protoOnlyGenerator;
        private InfrastructureInjector _injector;
        private ProtocolScanner _protocolScanner; // 协议扫描器

        private string _clientInfraPath = "Game/Client/ClientInfrastructure.cs";
        private string _serverInfraPath = "Game/Server/GlobalInfrastructure.cs";
        private string _serverRoomRegistryPath = "Game/Server/Room/RoomComponentRegistry.cs";
        private string _clientRoomRegistryPath = "Game/Client/Room/ClientRoomComponentRegistry.cs";

        // ── 颜色 ──────────────────────────────────────────────────
        private static readonly Color ColSidebar = new Color(0.16f, 0.16f, 0.16f);
        private static readonly Color ColContent = new Color(0.20f, 0.20f, 0.20f);
        private static readonly Color ColNavActive = new Color(0.18f, 0.42f, 0.70f);
        private static readonly Color ColNavHover = new Color(0.26f, 0.26f, 0.26f);
        private static readonly Color ColDivider = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColSectionBar = new Color(0.25f, 0.50f, 0.80f);
        private static readonly Color ColRequired = new Color(0.85f, 0.35f, 0.35f);
        private static readonly Color ColOptional = new Color(0.35f, 0.72f, 0.40f);
        private static readonly Color ColLogError = new Color(0.95f, 0.40f, 0.35f);
        private static readonly Color ColLogWarn = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color ColLogOk = new Color(0.45f, 0.85f, 0.55f);
        private static readonly Color ColConflict = new Color(0.45f, 0.15f, 0.15f); // 冲突行背景色

        // ── 样式缓存 ──────────────────────────────────────────────
        private GUIStyle _styleHeader;
        private GUIStyle _styleSectionLabel;
        private GUIStyle _styleNavLabel;
        private GUIStyle _styleNavLabelActive;
        private GUIStyle _styleFieldLabel;
        private GUIStyle _styleLogError;
        private GUIStyle _styleLogWarn;
        private GUIStyle _styleLogOk;
        private GUIStyle _styleActionBg;
        private bool _stylesReady;

        // ── Unity 生命周期 ────────────────────────────────────────
        private void OnEnable()
        {
            _fileWriteService = new FileWriteService();
            _globalGenerator = new GlobalModuleGenerator(_fileWriteService);
            _roomGenerator = new RoomComponentGenerator(_fileWriteService);
            _protoOnlyGenerator = new ProtoOnlyGenerator(_fileWriteService);
            _injector = new InfrastructureInjector();

            // 初始化并执行全量扫描
            _protocolScanner = new ProtocolScanner();
            _protocolScanner.Scan();

            LoadPrefs();
        }

        private void OnDisable() => SavePrefs();

        private void OnFocus()
        {
            // 窗口获得焦点时刷新扫描，确保获取最新的代码变动
            if (_protocolScanner != null)
            {
                _protocolScanner.Scan();
            }
        }

        private void OnGUI()
        {
            BuildStyles();

            // 侧边栏与主内容区背景用绝对坐标绘制，完全脱离 Layout 流
            EditorGUI.DrawRect(new Rect(0, 0, SidebarWidth, position.height), ColSidebar);
            EditorGUI.DrawRect(new Rect(SidebarWidth, 0, position.width - SidebarWidth, position.height), ColContent);

            EditorGUILayout.BeginHorizontal();
            {
                DrawSidebar();
                DrawMainContent();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════════
        // 样式构建
        // ══════════════════════════════════════════════════════════
        private void BuildStyles()
        {
            if (_stylesReady) return;

            _styleHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.90f, 0.90f, 0.90f) },
                margin = new RectOffset(0, 0, 4, 2)
            };
            _styleSectionLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
            };
            _styleNavLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.75f, 0.75f, 0.75f) },
                alignment = TextAnchor.MiddleLeft
            };
            _styleNavLabelActive = new GUIStyle(_styleNavLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _styleFieldLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.72f, 0.82f, 0.95f) },
                alignment = TextAnchor.MiddleLeft
            };
            _styleLogError = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, wordWrap = true,
                normal = { textColor = ColLogError }
            };
            _styleLogWarn = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, wordWrap = true,
                normal = { textColor = ColLogWarn }
            };
            _styleLogOk = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, wordWrap = true,
                normal = { textColor = ColLogOk }
            };
            _styleActionBg = new GUIStyle
            {
                normal = { background = MakeTex(1, 1, new Color(0.14f, 0.14f, 0.14f)) }
            };

            _stylesReady = true;
        }

        // ══════════════════════════════════════════════════════════
        // 侧边栏
        // ══════════════════════════════════════════════════════════
        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
            {
                GUILayout.Space(14f);
                DrawSidebarGroupLabel("模板类型");
                DrawNavItem("全局模块", Panel.GlobalModule, new Color(0.30f, 0.80f, 0.70f));
                DrawNavItem("房间业务组件", Panel.RoomComponent, new Color(0.78f, 0.52f, 0.78f));
                DrawNavItem("协议定义", Panel.ProtoOnly, new Color(0.88f, 0.88f, 0.50f));

                GUILayout.Space(8f);
                DrawSidebarGroupLabel("工具");
                DrawNavItem("批量生成", Panel.Batch, new Color(0.96f, 0.55f, 0.40f));
                DrawNavItem("生成日志", Panel.Log, new Color(0.50f, 0.75f, 0.98f));

                GUILayout.FlexibleSpace();
                GUILayout.Label("StellarNet Scaffold", EditorStyles.centeredGreyMiniLabel);
                GUILayout.Label("v1.0.1", EditorStyles.centeredGreyMiniLabel);
                GUILayout.Space(10f);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSidebarGroupLabel(string text)
        {
            GUILayout.Space(4f);
            Rect r = GUILayoutUtility.GetRect(SidebarWidth, 18f);
            GUI.Label(new Rect(r.x + 12f, r.y, r.width, r.height), text.ToUpper(), _styleSectionLabel);
            EditorGUI.DrawRect(new Rect(r.x + 8f, r.yMax - 1f, r.width - 16f, 1f), ColDivider);
            GUILayout.Space(2f);
        }

        private void DrawNavItem(string label, Panel target, Color dotColor)
        {
            bool isActive = _currentPanel == target;
            Rect rect = GUILayoutUtility.GetRect(SidebarWidth, NavItemHeight);

            if (isActive)
            {
                EditorGUI.DrawRect(rect, ColNavActive);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), new Color(0.45f, 0.75f, 1f));
            }
            else if (rect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rect, ColNavHover);
                Repaint();
            }

            float dotSize = 7f;
            float dotY = rect.y + (rect.height - dotSize) * 0.5f;
            EditorGUI.DrawRect(new Rect(rect.x + 14f, dotY, dotSize, dotSize), dotColor);

            GUI.Label(
                new Rect(rect.x + 28f, rect.y, rect.width - 32f, rect.height),
                label,
                isActive ? _styleNavLabelActive : _styleNavLabel);

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                _currentPanel = target;
        }

        // ══════════════════════════════════════════════════════════
        // 主内容区分发
        // ══════════════════════════════════════════════════════════
        private void DrawMainContent()
        {
            EditorGUILayout.BeginVertical();
            {
                switch (_currentPanel)
                {
                    case Panel.GlobalModule: DrawGlobalModulePanel(); break;
                    case Panel.RoomComponent: DrawRoomComponentPanel(); break;
                    case Panel.ProtoOnly: DrawProtoOnlyPanel(); break;
                    case Panel.Batch: DrawBatchPanel(); break;
                    case Panel.Log: DrawLogPanel(); break;
                }
            }
            EditorGUILayout.EndVertical();
        }

        // ══════════════════════════════════════════════════════════
        // 全局模块面板
        // ══════════════════════════════════════════════════════════
        private void DrawGlobalModulePanel()
        {
            DrawPanelHeader("全局模块", "生成 Model + Handle 双端文件，Handle 自动注册到 GlobalMessageRegistrar。");

            _globalScroll = EditorGUILayout.BeginScrollView(_globalScroll);
            {
                GUILayout.Space(6f);
                DrawSectionHeader("基础信息");
                _globalConfig.ModuleName = DrawField("模块名称", _globalConfig.ModuleName, "例：Leaderboard");
                _globalConfig.ClientNamespace = DrawField("客户端命名空间", _globalConfig.ClientNamespace);
                _globalConfig.ServerNamespace = DrawField("服务端命名空间", _globalConfig.ServerNamespace);
                _globalConfig.ProtoNamespace = DrawField("协议命名空间", _globalConfig.ProtoNamespace);
                DrawEnumField("生成端", ref _globalConfig.Target);

                DrawSectionHeader("输出路径");
                _globalConfig.ClientOutputPath = DrawFolderField("客户端输出路径", _globalConfig.ClientOutputPath);
                _globalConfig.ServerOutputPath = DrawFolderField("服务端输出路径", _globalConfig.ServerOutputPath);
                _globalConfig.ProtoOutputPath = DrawFolderField("协议输出路径", _globalConfig.ProtoOutputPath);

                DrawSectionHeader("生成选项");
                BeginOptionBox();
                {
                    DrawOption("客户端 Model", ref _globalConfig.GenClientModel, true);
                    DrawOption("客户端 Handle", ref _globalConfig.GenClientHandle, true);
                    DrawOption("服务端 Model", ref _globalConfig.GenServerModel, true);
                    DrawOption("服务端 Handle", ref _globalConfig.GenServerHandle, true);
                    DrawOption("协议文件", ref _globalConfig.GenProtoFile, false);
                    DrawOption("RegisterAll / UnregisterAll 桩", ref _globalConfig.GenRegisterStub, true);
                    DrawOption("IGlobalService 标记 + ServiceLocator 注册桩", ref _globalConfig.GenServiceLocatorStub,
                        false);
                    DrawOption("注入到 Infrastructure（需配置锚点）", ref _globalConfig.InjectToInfrastructure, false);
                }
                EndOptionBox();

                if (_globalConfig.InjectToInfrastructure)
                {
                    DrawSectionHeader("Infrastructure 注入路径");
                    DrawInfoBox("目标文件中需包含锚点注释：// [SCAFFOLD_INJECT:{标记名}]");
                    _clientInfraPath = DrawFileField("客户端 Infrastructure", _clientInfraPath);
                    _serverInfraPath = DrawFileField("服务端 Infrastructure", _serverInfraPath);
                }

                DrawSectionHeader("协议配置");
                _globalConfig.StartMessageId = DrawIntField("起始 MessageId", _globalConfig.StartMessageId,
                    "框架保留 0-9999，业务从 10000 起");

                GUILayout.Space(4f);
                DrawProtoList(_globalConfig.Protocols, ref _globalProtoScroll, _globalConfig.StartMessageId,
                    ProtoDirection.C2S_Global);

                GUILayout.Space(16f);
            }
            EditorGUILayout.EndScrollView();

            DrawActionBar(
                $"将生成约 {EstimateGlobalFileCount(_globalConfig)} 个文件",
                "生成代码",
                ExecuteGlobalGenerate,
                () =>
                {
                    if (ConfirmReset()) _globalConfig = new GlobalModuleConfig();
                });
        }

        // ══════════════════════════════════════════════════════════
        // 房间组件面板
        // ══════════════════════════════════════════════════════════
        private void DrawRoomComponentPanel()
        {
            DrawPanelHeader("房间业务组件",
                "实现 IInitializableRoomComponent，通过 RoomComponentRegistry 注册工厂，StableComponentId 必须与客户端保持一致。");

            _roomScroll = EditorGUILayout.BeginScrollView(_roomScroll);
            {
                GUILayout.Space(6f);
                DrawSectionHeader("基础信息");
                _roomConfig.ComponentName = DrawField("组件名称", _roomConfig.ComponentName, "例：TurnSystem");
                _roomConfig.StableComponentId = DrawField("StableComponentId", _roomConfig.StableComponentId,
                    "全局唯一且跨版本稳定，例：room.turn_system");
                _roomConfig.ServerNamespace = DrawField("服务端命名空间", _roomConfig.ServerNamespace);
                _roomConfig.ClientNamespace = DrawField("客户端命名空间", _roomConfig.ClientNamespace);
                _roomConfig.ProtoNamespace = DrawField("协议命名空间", _roomConfig.ProtoNamespace);
                DrawEnumField("生成端", ref _roomConfig.Target);

                DrawSectionHeader("输出路径");
                _roomConfig.ServerOutputPath = DrawFolderField("服务端输出路径", _roomConfig.ServerOutputPath);
                _roomConfig.ClientOutputPath = DrawFolderField("客户端输出路径", _roomConfig.ClientOutputPath);
                _roomConfig.ProtoOutputPath = DrawFolderField("协议输出路径", _roomConfig.ProtoOutputPath);

                DrawSectionHeader("生成选项");
                BeginOptionBox();
                {
                    DrawOption("服务端 Handle（IInitializableRoomComponent）", ref _roomConfig.GenServerHandle, true);
                    DrawOption("服务端 Model", ref _roomConfig.GenServerModel, true);
                    DrawOption("客户端 Handle（IInitializableClientRoomComponent）", ref _roomConfig.GenClientHandle, true);
                    DrawOption("客户端 Model", ref _roomConfig.GenClientModel, false);
                    DrawOption("GetHandlerBindings 桩", ref _roomConfig.GenHandlerBindingsStub, true);
                    DrawOption("全部生命周期回调桩（OnRoomCreate/Destroy 等）", ref _roomConfig.GenLifecycleCallbackStubs, true);
                    DrawOption("协议文件", ref _roomConfig.GenProtoFile, false);
                    DrawOption("IRoomService 接口 + ServiceLocator 注册桩", ref _roomConfig.GenServiceLocatorStub, false);
                    DrawOption("RoomEventBus 订阅桩", ref _roomConfig.GenEventBusStub, false);
                    DrawOption("重连快照补发桩（SendSnapshotToMember）", ref _roomConfig.GenReconnectSnapshotStub, false);
                    DrawOption("注入到 Registry（需配置锚点）", ref _roomConfig.InjectToRegistry, false);
                }
                EndOptionBox();

                if (_roomConfig.InjectToRegistry)
                {
                    DrawSectionHeader("Registry 注入路径");
                    DrawInfoBox("目标文件中需包含锚点注释：// [SCAFFOLD_INJECT:{标记名}]");
                    _serverRoomRegistryPath = DrawFileField("服务端 Infra", _serverRoomRegistryPath);
                    _clientRoomRegistryPath = DrawFileField("客户端 Infra", _clientRoomRegistryPath);
                }

                DrawSectionHeader("协议配置");
                _roomConfig.StartMessageId = DrawIntField("起始 MessageId", _roomConfig.StartMessageId, "建议按组件划分号段");

                GUILayout.Space(4f);
                DrawProtoList(_roomConfig.Protocols, ref _roomProtoScroll, _roomConfig.StartMessageId,
                    ProtoDirection.C2S_Room);

                GUILayout.Space(16f);
            }
            EditorGUILayout.EndScrollView();

            DrawActionBar(
                $"将生成约 {EstimateRoomFileCount(_roomConfig)} 个文件",
                "生成代码",
                ExecuteRoomGenerate,
                () =>
                {
                    if (ConfirmReset()) _roomConfig = new RoomComponentConfig();
                });
        }

        // ══════════════════════════════════════════════════════════
        // 协议定义面板
        // ══════════════════════════════════════════════════════════
        private void DrawProtoOnlyPanel()
        {
            DrawPanelHeader("协议定义", "独立生成协议聚合文件，适合先规划协议再实现业务的开发流程。");

            _protoScroll = EditorGUILayout.BeginScrollView(_protoScroll);
            {
                GUILayout.Space(6f);
                DrawSectionHeader("基础信息");
                _protoOnlyFileName = DrawField("文件名", _protoOnlyFileName, "不含 .cs 扩展名");
                _protoOnlyModuleName = DrawField("模块名", _protoOnlyModuleName, "用于文件头注释");
                _protoOnlyOutputPath = DrawFolderField("输出路径", _protoOnlyOutputPath);
                _protoOnlyNamespace = DrawField("命名空间", _protoOnlyNamespace);

                DrawSectionHeader("域归属");
                string[] domainOptions = { "Global（全局域）", "Room（房间域）" };
                int domainIndex = _protoOnlyDomain == "Room（房间域）" ? 1 : 0;
                EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
                {
                    GUILayout.Label("域归属", _styleFieldLabel, GUILayout.Width(LabelWidth));
                    domainIndex = EditorGUILayout.Popup(domainIndex, domainOptions);
                }
                EditorGUILayout.EndHorizontal();
                _protoOnlyDomain = domainOptions[domainIndex];

                DrawSectionHeader("协议列表");
                ProtoDirection defaultDir = domainIndex == 0 ? ProtoDirection.C2S_Global : ProtoDirection.C2S_Room;
                DrawProtoList(_protoOnlyList, ref _protoOnlyProtoScroll, 10000, defaultDir);

                GUILayout.Space(16f);
            }
            EditorGUILayout.EndScrollView();

            DrawActionBar(
                $"共 {_protoOnlyList.Count} 条协议",
                "生成协议文件",
                ExecuteProtoOnlyGenerate,
                () =>
                {
                    if (EditorUtility.DisplayDialog("确认清空", "将清空协议列表，确认？", "确认", "取消"))
                        _protoOnlyList.Clear();
                },
                resetLabel: "清空列表");
        }

        // ══════════════════════════════════════════════════════════
        // 批量生成面板
        // ══════════════════════════════════════════════════════════
        private void DrawBatchPanel()
        {
            DrawPanelHeader("批量生成", "将多个模块加入队列后统一生成，适合新项目初始化时一次性创建多个模块。");
            DrawWarnBox("批量生成会同时创建多个模块，请确认所有配置正确后再执行，生成后无法自动撤销。");

            _batchScroll = EditorGUILayout.BeginScrollView(_batchScroll);
            {
                GUILayout.Space(6f);
                DrawSectionHeader("待生成队列");

                if (_batchQueue.Count == 0)
                {
                    EditorGUILayout.HelpBox("队列为空，请将全局模块或房间组件配置好后点击「加入队列」。", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    {
                        GUILayout.Label("模块名称", EditorStyles.toolbarButton, GUILayout.Width(150f));
                        GUILayout.Label("类型", EditorStyles.toolbarButton, GUILayout.Width(90f));
                        GUILayout.Label("生成端", EditorStyles.toolbarButton, GUILayout.Width(80f));
                        GUILayout.Label("状态", EditorStyles.toolbarButton, GUILayout.Width(70f));
                        GUILayout.Label("操作", EditorStyles.toolbarButton, GUILayout.Width(50f));
                    }
                    EditorGUILayout.EndHorizontal();

                    int removeIndex = -1;
                    for (int i = 0; i < _batchQueue.Count; i++)
                    {
                        var item = _batchQueue[i];
                        Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(24f));
                        EditorGUI.DrawRect(rowRect, i % 2 == 0
                            ? new Color(0.22f, 0.22f, 0.22f)
                            : new Color(0.19f, 0.19f, 0.19f));
                        {
                            GUILayout.Label(item.DisplayName, GUILayout.Width(150f));
                            GUILayout.Label(GetItemTypeLabel(item.Type), GUILayout.Width(90f));
                            GUILayout.Label(GetTargetLabel(item), GUILayout.Width(80f));
                            GUILayout.Label(GetStatusLabel(item.Status), GUILayout.Width(70f));

                            GUI.backgroundColor = new Color(0.75f, 0.25f, 0.25f);
                            if (GUILayout.Button("移除", GUILayout.Width(44f)))
                                removeIndex = i;
                            GUI.backgroundColor = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    if (removeIndex >= 0)
                        _batchQueue.RemoveAt(removeIndex);
                }

                GUILayout.Space(8f);
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("将当前全局模块加入队列", GUILayout.Height(26f)))
                        AddGlobalToBatch();
                    if (GUILayout.Button("将当前房间组件加入队列", GUILayout.Height(26f)))
                        AddRoomToBatch();
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(16f);
            }
            EditorGUILayout.EndScrollView();

            DrawDivider();
            EditorGUILayout.BeginHorizontal(_styleActionBg, GUILayout.Height(ActionBarHeight));
            {
                GUILayout.Space(10f);
                GUI.backgroundColor = new Color(0.75f, 0.25f, 0.25f);
                if (GUILayout.Button("清空队列", GUILayout.Width(80f), GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog("确认清空", "将清空批量生成队列，确认？", "确认", "取消"))
                        _batchQueue.Clear();
                }

                GUI.backgroundColor = Color.white;

                GUILayout.FlexibleSpace();
                GUILayout.Label($"队列中共 {_batchQueue.Count} 个任务", EditorStyles.miniLabel);

                GUILayout.Space(8f);
                GUI.backgroundColor = new Color(0.22f, 0.52f, 0.85f);
                if (GUILayout.Button("执行批量生成", GUILayout.Width(100f), GUILayout.Height(28f)))
                    ExecuteBatchGenerate();
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10f);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════════
        // 生成日志面板
        // ══════════════════════════════════════════════════════════
        private void DrawLogPanel()
        {
            DrawPanelHeader("生成日志", "记录每次生成操作的详细结果，包含写入文件路径、警告与错误信息。");

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空日志", GUILayout.Width(80f)))
                    _logLines.Clear();
                GUILayout.Space(10f);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll,
                EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            {
                if (_logLines.Count == 0)
                {
                    GUILayout.Label("暂无日志记录。", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    foreach (var line in _logLines)
                    {
                        GUIStyle s = line.StartsWith("[ERROR]") ? _styleLogError
                            : line.StartsWith("[WARN]") ? _styleLogWarn
                            : _styleLogOk;
                        GUILayout.Label(line, s);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ══════════════════════════════════════════════════════════
        // 协议列表通用绘制（集成 ProtocolScanner）
        // ══════════════════════════════════════════════════════════
        private void DrawProtoList(
            List<ProtoDefinition> protos,
            ref Vector2 scroll,
            int startId,
            ProtoDirection defaultDir)
        {
            // 获取建议的下一个可用 ID
            int suggestedId = _protocolScanner.GetNextAvailableId(startId);

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Label($"建议可用 ID: {suggestedId}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("刷新扫描", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    _protocolScanner.Scan();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("MessageId", EditorStyles.toolbarButton, GUILayout.Width(85f));
                GUILayout.Label("类名", EditorStyles.toolbarButton, GUILayout.Width(200f));
                GUILayout.Label("方向", EditorStyles.toolbarButton, GUILayout.Width(115f));
                GUILayout.Label("注释", EditorStyles.toolbarButton);
                GUILayout.Label("", EditorStyles.toolbarButton, GUILayout.Width(26f));
            }
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(150f));
            {
                int removeIndex = -1;
                for (int i = 0; i < protos.Count; i++)
                {
                    var p = protos[i];

                    // 检查 ID 冲突：是否已被工程中其他协议占用
                    bool isConflict = _protocolScanner.UsedIds.Contains(p.MessageId);

                    // 设置行背景色：冲突显示红色，否则显示交替色
                    Color rowColor;
                    if (isConflict)
                    {
                        rowColor = ColConflict;
                    }
                    else
                    {
                        rowColor = i % 2 == 0 ? new Color(0.21f, 0.21f, 0.21f) : new Color(0.18f, 0.18f, 0.18f);
                    }

                    Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20f));
                    EditorGUI.DrawRect(rowRect, rowColor);
                    {
                        int newId = EditorGUILayout.IntField(p.MessageId, GUILayout.Width(85f));
                        if (newId != p.MessageId) p.MessageId = newId;

                        p.ClassName = EditorGUILayout.TextField(p.ClassName, GUILayout.Width(200f));
                        p.Direction = (ProtoDirection)EditorGUILayout.EnumPopup(p.Direction, GUILayout.Width(115f));
                        p.Comment = EditorGUILayout.TextField(p.Comment);

                        GUI.backgroundColor = new Color(0.75f, 0.25f, 0.25f);
                        if (GUILayout.Button("X", GUILayout.Width(22f)))
                            removeIndex = i;
                        GUI.backgroundColor = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (removeIndex >= 0)
                    protos.RemoveAt(removeIndex);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(2f);
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.35f);
                if (GUILayout.Button("+ 添加协议", GUILayout.Width(90f), GUILayout.Height(22f)))
                {
                    // 自动分配不冲突的 ID
                    int nextId = _protocolScanner.GetNextAvailableId(startId);
                    // 还要避开当前列表中已有的 ID，防止同次生成内冲突
                    foreach (var p in protos)
                    {
                        if (p.MessageId >= nextId) nextId = p.MessageId + 1;
                    }

                    // 再次检查 Scanner，确保 +1 后没有撞上已有的
                    nextId = _protocolScanner.GetNextAvailableId(nextId);

                    protos.Add(new ProtoDefinition(nextId, "C2S_NewMessage", defaultDir));
                }

                GUI.backgroundColor = Color.white;

                if (protos.Count > 0)
                    GUILayout.Label($"共 {protos.Count} 条", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════════
        // 生成执行逻辑
        // ══════════════════════════════════════════════════════════
        private void ExecuteGlobalGenerate()
        {
            _fileWriteService.Clear();
            var result = new GenerateResult();

            // 传入 _protocolScanner 进行全量防重校验
            if (!ScaffoldValidator.ValidateGlobalModule(_globalConfig, _protocolScanner, result))
            {
                FlushResultToLog(result, "全局模块");
                _currentPanel = Panel.Log;
                return;
            }

            _globalGenerator.Generate(_globalConfig, result);

            if (_globalConfig.InjectToInfrastructure)
            {
                _injector.InjectClientGlobalModule(_globalConfig, _clientInfraPath, result);
                _injector.InjectServerGlobalModule(_globalConfig, _serverInfraPath, result);
            }

            _fileWriteService.FlushAll(result);
            result.Success = result.Errors.Count == 0;

            FlushResultToLog(result, "全局模块");
            _currentPanel = Panel.Log;

            EditorUtility.DisplayDialog(
                result.Success ? "生成完成" : "生成失败",
                result.Success
                    ? $"全局模块 {_globalConfig.ModuleName} 生成成功。\n写入 {result.WrittenFiles.Count} 个文件，修改 {result.ModifiedFiles.Count} 个文件。"
                    : $"生成过程中出现 {result.Errors.Count} 个错误，请查看生成日志。",
                "确认");
        }

        private void ExecuteRoomGenerate()
        {
            _fileWriteService.Clear();
            var result = new GenerateResult();

            // 传入 _protocolScanner 进行全量防重校验
            if (!ScaffoldValidator.ValidateRoomComponent(_roomConfig, _protocolScanner, result))
            {
                FlushResultToLog(result, "房间组件");
                _currentPanel = Panel.Log;
                return;
            }

            _roomGenerator.Generate(_roomConfig, result);

            if (_roomConfig.InjectToRegistry)
            {
                _injector.InjectServerRoomComponent(_roomConfig, _serverRoomRegistryPath, result);
                _injector.InjectClientRoomComponent(_roomConfig, _clientRoomRegistryPath, result);
            }

            _fileWriteService.FlushAll(result);
            result.Success = result.Errors.Count == 0;

            FlushResultToLog(result, "房间组件");
            _currentPanel = Panel.Log;

            EditorUtility.DisplayDialog(
                result.Success ? "生成完成" : "生成失败",
                result.Success
                    ? $"房间组件 {_roomConfig.ComponentName} 生成成功。\n写入 {result.WrittenFiles.Count} 个文件，修改 {result.ModifiedFiles.Count} 个文件。"
                    : $"生成过程中出现 {result.Errors.Count} 个错误，请查看生成日志。",
                "确认");
        }

        private void ExecuteProtoOnlyGenerate()
        {
            _fileWriteService.Clear();
            var result = new GenerateResult();

            // ProtoOnly 暂未封装统一 Validator，此处手动简单校验一下 ID 冲突
            // 实际工程建议也封装进 ScaffoldValidator
            bool hasConflict = false;
            foreach (var p in _protoOnlyList)
            {
                if (_protocolScanner.UsedIds.Contains(p.MessageId))
                {
                    result.AddError($"协议 ID {p.MessageId} 已被现有代码占用，请修改。");
                    hasConflict = true;
                }
            }

            if (hasConflict)
            {
                FlushResultToLog(result, "协议定义（前置校验）");
                _currentPanel = Panel.Log;
                return;
            }

            _protoOnlyGenerator.Generate(
                _protoOnlyFileName, _protoOnlyOutputPath, _protoOnlyNamespace,
                _protoOnlyModuleName, _protoOnlyDomain, _protoOnlyList, result);

            _fileWriteService.FlushAll(result);
            result.Success = result.Errors.Count == 0;

            FlushResultToLog(result, "协议定义");
            _currentPanel = Panel.Log;

            EditorUtility.DisplayDialog(
                result.Success ? "生成完成" : "生成失败",
                result.Success
                    ? $"协议文件 {_protoOnlyFileName}.cs 生成成功。"
                    : $"生成过程中出现 {result.Errors.Count} 个错误，请查看生成日志。",
                "确认");
        }

        private void ExecuteBatchGenerate()
        {
            if (_batchQueue.Count == 0)
            {
                EditorUtility.DisplayDialog("队列为空", "批量生成队列中没有任务，请先添加任务。", "确认");
                return;
            }

            if (!EditorUtility.DisplayDialog("确认批量生成",
                    $"将执行 {_batchQueue.Count} 个生成任务，确认？", "确认", "取消"))
                return;

            var batchResult = new GenerateResult();

            // 传入 _protocolScanner 进行全量防重校验
            if (!ScaffoldValidator.ValidateBatchQueue(_batchQueue, _protocolScanner, batchResult))
            {
                FlushResultToLog(batchResult, "批量生成（前置校验）");
                _currentPanel = Panel.Log;
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var item in _batchQueue)
            {
                _fileWriteService.Clear();
                var itemResult = new GenerateResult();
                item.Status = BatchQueueItem.GenerateStatus.Generating;

                if (item.Type == BatchQueueItem.ItemType.GlobalModule && item.GlobalConfig != null)
                    _globalGenerator.Generate(item.GlobalConfig, itemResult);
                else if (item.Type == BatchQueueItem.ItemType.RoomComponent && item.RoomConfig != null)
                    _roomGenerator.Generate(item.RoomConfig, itemResult);

                _fileWriteService.FlushAll(itemResult);
                itemResult.Success = itemResult.Errors.Count == 0;

                if (itemResult.Success)
                {
                    item.Status = BatchQueueItem.GenerateStatus.Done;
                    successCount++;
                }
                else
                {
                    item.Status = BatchQueueItem.GenerateStatus.Failed;
                    item.ErrorMessage = string.Join("\n", itemResult.Errors);
                    failCount++;
                }

                foreach (var e in itemResult.Errors) batchResult.AddError(e);
                foreach (var w in itemResult.Warnings) batchResult.AddWarning(w);
                foreach (var f in itemResult.WrittenFiles) batchResult.AddWritten(f);
                foreach (var f in itemResult.ModifiedFiles) batchResult.AddModified(f);
            }

            batchResult.Success = failCount == 0;
            FlushResultToLog(batchResult, "批量生成");
            _currentPanel = Panel.Log;

            EditorUtility.DisplayDialog("批量生成完成",
                $"成功：{successCount} 个，失败：{failCount} 个。\n共写入 {batchResult.WrittenFiles.Count} 个文件。\n详细信息请查看生成日志。",
                "确认");
        }

        // ══════════════════════════════════════════════════════════
        // 批量队列辅助
        // ══════════════════════════════════════════════════════════
        private void AddGlobalToBatch()
        {
            var r = new GenerateResult();
            // 加入队列前也进行一次校验，包含 ID 查重
            if (!ScaffoldValidator.ValidateGlobalModule(_globalConfig, _protocolScanner, r))
            {
                EditorUtility.DisplayDialog("配置校验失败",
                    "当前全局模块配置存在错误，请修正后再加入队列。\n" + string.Join("\n", r.Errors), "确认");
                return;
            }

            var copy = DeepCopyGlobalConfig(_globalConfig);
            _batchQueue.Add(new BatchQueueItem
            {
                Type = BatchQueueItem.ItemType.GlobalModule,
                DisplayName = copy.ModuleName,
                GlobalConfig = copy,
                Status = BatchQueueItem.GenerateStatus.Pending
            });
        }

        private void AddRoomToBatch()
        {
            var r = new GenerateResult();
            // 加入队列前也进行一次校验，包含 ID 查重
            if (!ScaffoldValidator.ValidateRoomComponent(_roomConfig, _protocolScanner, r))
            {
                EditorUtility.DisplayDialog("配置校验失败",
                    "当前房间组件配置存在错误，请修正后再加入队列。\n" + string.Join("\n", r.Errors), "确认");
                return;
            }

            var copy = DeepCopyRoomConfig(_roomConfig);
            _batchQueue.Add(new BatchQueueItem
            {
                Type = BatchQueueItem.ItemType.RoomComponent,
                DisplayName = copy.ComponentName,
                RoomConfig = copy,
                Status = BatchQueueItem.GenerateStatus.Pending
            });
        }

        // ══════════════════════════════════════════════════════════
        // 日志写入
        // ══════════════════════════════════════════════════════════
        private void FlushResultToLog(GenerateResult result, string context)
        {
            _logLines.Add($"─── {context} 生成结果 [{System.DateTime.Now:HH:mm:ss}] ───");
            foreach (var e in result.Errors) _logLines.Add($"[ERROR] {e}");
            foreach (var w in result.Warnings) _logLines.Add($"[WARN]  {w}");
            foreach (var f in result.WrittenFiles) _logLines.Add($"[OK]    写入：{f}");
            foreach (var f in result.ModifiedFiles) _logLines.Add($"[OK]    修改：{f}");

            if (result.Errors.Count == 0 && result.Warnings.Count == 0)
                _logLines.Add("[OK]    生成完成，无错误无警告。");

            _logLines.Add("");
        }

        // ══════════════════════════════════════════════════════════
        // UI 通用组件
        // ══════════════════════════════════════════════════════════
        private void DrawPanelHeader(string title, string desc)
        {
            Rect topBar = GUILayoutUtility.GetRect(0f, 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(topBar, ColNavActive);

            GUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();
            {
                GUILayout.Space(12f);
                EditorGUILayout.BeginVertical();
                {
                    GUILayout.Label(title, _styleHeader);
                    GUILayout.Label(desc, EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);
            DrawDivider();
            GUILayout.Space(4f);
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Space(10f);
            Rect r = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(r.x + 10f, r.y + 3f, 3f, r.height - 6f), ColSectionBar);
            GUI.Label(new Rect(r.x + 18f, r.y, r.width - 18f, r.height), title.ToUpper(), _styleSectionLabel);
            GUILayout.Space(2f);
        }

        private void DrawDivider()
        {
            Rect r = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, ColDivider);
        }

        private string DrawField(string label, string value, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Space(10f);
            GUILayout.Label(new GUIContent(label, tooltip), _styleFieldLabel, GUILayout.Width(LabelWidth));
            string result = EditorGUILayout.TextField(value);
            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private string DrawFolderField(string label, string value, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Space(10f);
            GUILayout.Label(new GUIContent(label, tooltip), _styleFieldLabel, GUILayout.Width(LabelWidth));
            string newValue = EditorGUILayout.TextField(value);
            if (GUILayout.Button("...", GUILayout.Width(FolderBtnWidth), GUILayout.Height(18f)))
            {
                string initPath = string.IsNullOrEmpty(value)
                    ? Application.dataPath
                    : Path.Combine(Application.dataPath, value).Replace('\\', '/');
                string selected = EditorUtility.OpenFolderPanel("选择输出目录", initPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    string relative = AbsToRelative(selected);
                    if (relative == null)
                    {
                        EditorUtility.DisplayDialog("路径非法",
                            "输出目录必须位于当前项目的 Assets 目录内，请重新选择。", "确认");
                    }
                    else
                    {
                        newValue = relative;
                    }
                }
            }

            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private string DrawFileField(string label, string value, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Space(10f);
            GUILayout.Label(new GUIContent(label, tooltip), _styleFieldLabel, GUILayout.Width(LabelWidth));
            string newValue = EditorGUILayout.TextField(value);
            if (GUILayout.Button("...", GUILayout.Width(FolderBtnWidth), GUILayout.Height(18f)))
            {
                string initDir = string.IsNullOrEmpty(value)
                    ? Application.dataPath
                    : Path.GetDirectoryName(Path.Combine(Application.dataPath, value).Replace('\\', '/'));
                string selected = EditorUtility.OpenFilePanel("选择目标文件", initDir, "cs");
                if (!string.IsNullOrEmpty(selected))
                {
                    string relative = AbsToRelative(selected);
                    if (relative == null)
                    {
                        EditorUtility.DisplayDialog("路径非法",
                            "目标文件必须位于当前项目的 Assets 目录内，请重新选择。", "确认");
                    }
                    else
                    {
                        newValue = relative;
                    }
                }
            }

            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private int DrawIntField(string label, int value, string tooltip = "")
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Space(10f);
            GUILayout.Label(new GUIContent(label, tooltip), _styleFieldLabel, GUILayout.Width(LabelWidth));
            int result = EditorGUILayout.IntField(value);
            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private void DrawEnumField<T>(string label, ref T value) where T : System.Enum
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(22f));
            GUILayout.Space(10f);
            GUILayout.Label(label, _styleFieldLabel, GUILayout.Width(LabelWidth));
            value = (T)EditorGUILayout.EnumPopup(value);
            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
        }

        private void BeginOptionBox()
        {
            GUILayout.Space(2f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(2f);
        }

        private void EndOptionBox()
        {
            GUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }

        private void DrawOption(string label, ref bool value, bool isRequired)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(20f));
            {
                GUILayout.Space(4f);
                value = EditorGUILayout.Toggle(value, GUILayout.Width(16f));
                GUILayout.Label(label, GUILayout.ExpandWidth(true));
                GUI.color = isRequired ? ColRequired : ColOptional;
                GUILayout.Label(isRequired ? "必须" : "可选", EditorStyles.miniLabel, GUILayout.Width(28f));
                GUI.color = Color.white;
                GUILayout.Space(4f);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInfoBox(string msg)
        {
            GUILayout.Space(2f);
            EditorGUILayout.HelpBox(msg, MessageType.Info);
            GUILayout.Space(2f);
        }

        private void DrawWarnBox(string msg)
        {
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10f);
            EditorGUILayout.HelpBox(msg, MessageType.Warning);
            GUILayout.Space(10f);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActionBar(
            string statusText,
            string confirmLabel,
            System.Action onConfirm,
            System.Action onReset,
            string resetLabel = "重置")
        {
            DrawDivider();
            EditorGUILayout.BeginHorizontal(_styleActionBg, GUILayout.Height(ActionBarHeight));
            {
                GUILayout.Space(10f);
                if (GUILayout.Button(resetLabel, GUILayout.Width(70f), GUILayout.Height(28f)))
                    onReset?.Invoke();

                GUILayout.FlexibleSpace();
                GUILayout.Label(statusText, EditorStyles.miniLabel);

                GUILayout.Space(8f);
                GUI.backgroundColor = new Color(0.22f, 0.52f, 0.85f);
                if (GUILayout.Button(confirmLabel, GUILayout.Width(90f), GUILayout.Height(28f)))
                    onConfirm?.Invoke();
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10f);
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool ConfirmReset()
        {
            return EditorUtility.DisplayDialog("确认重置", "将清空当前面板的所有配置，确认？", "确认", "取消");
        }

        // ══════════════════════════════════════════════════════════
        // 路径转换工具
        // ══════════════════════════════════════════════════════════
        private static string AbsToRelative(string absPath)
        {
            if (string.IsNullOrEmpty(absPath))
                return null;
            string normalized = absPath.Replace('\\', '/');
            string assetsRoot = Application.dataPath.Replace('\\', '/');
            if (!normalized.StartsWith(assetsRoot))
                return null;
            string relative = normalized.Substring(assetsRoot.Length).TrimStart('/');
            return relative;
        }

        // ══════════════════════════════════════════════════════════
        // 文件数量预估
        // ══════════════════════════════════════════════════════════
        private static int EstimateGlobalFileCount(GlobalModuleConfig cfg)
        {
            int c = 0;
            bool nc = cfg.Target != GenerateTarget.ServerOnly;
            bool ns = cfg.Target != GenerateTarget.ClientOnly;
            if (nc && cfg.GenClientModel) c++;
            if (nc && cfg.GenClientHandle) c++;
            if (ns && cfg.GenServerModel) c++;
            if (ns && cfg.GenServerHandle) c++;
            if (cfg.GenProtoFile) c++;
            return c;
        }

        private static int EstimateRoomFileCount(RoomComponentConfig cfg)
        {
            int c = 0;
            bool nc = cfg.Target != GenerateTarget.ServerOnly;
            bool ns = cfg.Target != GenerateTarget.ClientOnly;
            if (ns && cfg.GenServerModel) c++;
            if (ns && cfg.GenServerHandle) c++;
            if (nc && cfg.GenClientModel) c++;
            if (nc && cfg.GenClientHandle) c++;
            if (cfg.GenProtoFile) c++;
            return c;
        }

        // ══════════════════════════════════════════════════════════
        // 标签辅助
        // ══════════════════════════════════════════════════════════
        private static string GetItemTypeLabel(BatchQueueItem.ItemType t) => t switch
        {
            BatchQueueItem.ItemType.GlobalModule => "全局模块",
            BatchQueueItem.ItemType.RoomComponent => "房间组件",
            BatchQueueItem.ItemType.ProtoOnly => "协议定义",
            _ => "未知"
        };

        private static string GetTargetLabel(BatchQueueItem item)
        {
            var t = item.Type == BatchQueueItem.ItemType.GlobalModule
                ? item.GlobalConfig?.Target ?? GenerateTarget.BothSides
                : item.RoomConfig?.Target ?? GenerateTarget.BothSides;
            return t switch
            {
                GenerateTarget.BothSides => "双端",
                GenerateTarget.ClientOnly => "仅客户端",
                GenerateTarget.ServerOnly => "仅服务端",
            };
        }

        private static string GetStatusLabel(BatchQueueItem.GenerateStatus s) => s switch
        {
            BatchQueueItem.GenerateStatus.Pending => "待生成",
            BatchQueueItem.GenerateStatus.Generating => "生成中",
            BatchQueueItem.GenerateStatus.Done => "完成",
            BatchQueueItem.GenerateStatus.Failed => "失败",
            _ => "未知"
        };

        // ══════════════════════════════════════════════════════════
        // 深拷贝
        // ══════════════════════════════════════════════════════════
        private static GlobalModuleConfig DeepCopyGlobalConfig(GlobalModuleConfig src)
        {
            var dst = new GlobalModuleConfig
            {
                ModuleName = src.ModuleName,
                ClientNamespace = src.ClientNamespace,
                ServerNamespace = src.ServerNamespace,
                ProtoNamespace = src.ProtoNamespace,
                Target = src.Target,
                GenClientModel = src.GenClientModel,
                GenClientHandle = src.GenClientHandle,
                GenServerModel = src.GenServerModel,
                GenServerHandle = src.GenServerHandle,
                GenProtoFile = src.GenProtoFile,
                GenRegisterStub = src.GenRegisterStub,
                GenServiceLocatorStub = src.GenServiceLocatorStub,
                InjectToInfrastructure = src.InjectToInfrastructure,
                StartMessageId = src.StartMessageId,
                ClientOutputPath = src.ClientOutputPath,
                ServerOutputPath = src.ServerOutputPath,
                ProtoOutputPath = src.ProtoOutputPath
            };
            foreach (var p in src.Protocols)
                dst.Protocols.Add(new ProtoDefinition(p.MessageId, p.ClassName, p.Direction, p.Comment));
            return dst;
        }

        private static RoomComponentConfig DeepCopyRoomConfig(RoomComponentConfig src)
        {
            var dst = new RoomComponentConfig
            {
                ComponentName = src.ComponentName,
                StableComponentId = src.StableComponentId,
                ServerNamespace = src.ServerNamespace,
                ClientNamespace = src.ClientNamespace,
                ProtoNamespace = src.ProtoNamespace,
                Target = src.Target,
                GenServerHandle = src.GenServerHandle,
                GenServerModel = src.GenServerModel,
                GenClientHandle = src.GenClientHandle,
                GenClientModel = src.GenClientModel,
                GenHandlerBindingsStub = src.GenHandlerBindingsStub,
                GenLifecycleCallbackStubs = src.GenLifecycleCallbackStubs,
                GenIRoomServiceInterface = src.GenIRoomServiceInterface,
                GenServiceLocatorStub = src.GenServiceLocatorStub,
                GenEventBusStub = src.GenEventBusStub,
                GenProtoFile = src.GenProtoFile,
                GenReconnectSnapshotStub = src.GenReconnectSnapshotStub,
                InjectToRegistry = src.InjectToRegistry,
                StartMessageId = src.StartMessageId,
                ServerOutputPath = src.ServerOutputPath,
                ClientOutputPath = src.ClientOutputPath,
                ProtoOutputPath = src.ProtoOutputPath
            };
            foreach (var p in src.Protocols)
                dst.Protocols.Add(new ProtoDefinition(p.MessageId, p.ClassName, p.Direction, p.Comment));
            return dst;
        }

        // ══════════════════════════════════════════════════════════
        // 工具方法
        // ══════════════════════════════════════════════════════════
        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // ══════════════════════════════════════════════════════════
        // EditorPrefs 持久化
        // ══════════════════════════════════════════════════════════
        private const string PrefKeyClientInfra = "StellarScaffold_ClientInfraPath";
        private const string PrefKeyServerInfra = "StellarScaffold_ServerInfraPath";
        private const string PrefKeyServerRoomReg = "StellarScaffold_ServerRoomRegPath";
        private const string PrefKeyClientRoomReg = "StellarScaffold_ClientRoomRegPath";
        private const string PrefKeyGlobalModuleName = "StellarScaffold_GlobalModuleName";
        private const string PrefKeyGlobalClientNs = "StellarScaffold_GlobalClientNs";
        private const string PrefKeyGlobalServerNs = "StellarScaffold_GlobalServerNs";
        private const string PrefKeyGlobalProtoNs = "StellarScaffold_GlobalProtoNs";
        private const string PrefKeyGlobalClientPath = "StellarScaffold_GlobalClientPath";
        private const string PrefKeyGlobalServerPath = "StellarScaffold_GlobalServerPath";
        private const string PrefKeyGlobalProtoPath = "StellarScaffold_GlobalProtoPath";
        private const string PrefKeyRoomCompName = "StellarScaffold_RoomComponentName";
        private const string PrefKeyRoomStableId = "StellarScaffold_RoomStableId";
        private const string PrefKeyRoomServerNs = "StellarScaffold_RoomServerNs";
        private const string PrefKeyRoomClientNs = "StellarScaffold_RoomClientNs";
        private const string PrefKeyRoomProtoNs = "StellarScaffold_RoomProtoNs";
        private const string PrefKeyRoomServerPath = "StellarScaffold_RoomServerPath";
        private const string PrefKeyRoomClientPath = "StellarScaffold_RoomClientPath";
        private const string PrefKeyRoomProtoPath = "StellarScaffold_RoomProtoPath";
        private const string PrefKeyProtoFileName = "StellarScaffold_ProtoFileName";
        private const string PrefKeyProtoOutputPath = "StellarScaffold_ProtoOutputPath";
        private const string PrefKeyProtoNamespace = "StellarScaffold_ProtoNamespace";
        private const string PrefKeyProtoModuleName = "StellarScaffold_ProtoModuleName";

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefKeyClientInfra, _clientInfraPath);
            EditorPrefs.SetString(PrefKeyServerInfra, _serverInfraPath);
            EditorPrefs.SetString(PrefKeyServerRoomReg, _serverRoomRegistryPath);
            EditorPrefs.SetString(PrefKeyClientRoomReg, _clientRoomRegistryPath);

            EditorPrefs.SetString(PrefKeyGlobalModuleName, _globalConfig.ModuleName);
            EditorPrefs.SetString(PrefKeyGlobalClientNs, _globalConfig.ClientNamespace);
            EditorPrefs.SetString(PrefKeyGlobalServerNs, _globalConfig.ServerNamespace);
            EditorPrefs.SetString(PrefKeyGlobalProtoNs, _globalConfig.ProtoNamespace);
            EditorPrefs.SetString(PrefKeyGlobalClientPath, _globalConfig.ClientOutputPath);
            EditorPrefs.SetString(PrefKeyGlobalServerPath, _globalConfig.ServerOutputPath);
            EditorPrefs.SetString(PrefKeyGlobalProtoPath, _globalConfig.ProtoOutputPath);

            EditorPrefs.SetString(PrefKeyRoomCompName, _roomConfig.ComponentName);
            EditorPrefs.SetString(PrefKeyRoomStableId, _roomConfig.StableComponentId);
            EditorPrefs.SetString(PrefKeyRoomServerNs, _roomConfig.ServerNamespace);
            EditorPrefs.SetString(PrefKeyRoomClientNs, _roomConfig.ClientNamespace);
            EditorPrefs.SetString(PrefKeyRoomProtoNs, _roomConfig.ProtoNamespace);
            EditorPrefs.SetString(PrefKeyRoomServerPath, _roomConfig.ServerOutputPath);
            EditorPrefs.SetString(PrefKeyRoomClientPath, _roomConfig.ClientOutputPath);
            EditorPrefs.SetString(PrefKeyRoomProtoPath, _roomConfig.ProtoOutputPath);

            EditorPrefs.SetString(PrefKeyProtoFileName, _protoOnlyFileName);
            EditorPrefs.SetString(PrefKeyProtoOutputPath, _protoOnlyOutputPath);
            EditorPrefs.SetString(PrefKeyProtoNamespace, _protoOnlyNamespace);
            EditorPrefs.SetString(PrefKeyProtoModuleName, _protoOnlyModuleName);
        }

        private void LoadPrefs()
        {
            // Infrastructure 注入路径
            _clientInfraPath = EditorPrefs.GetString(PrefKeyClientInfra, _clientInfraPath);
            _serverInfraPath = EditorPrefs.GetString(PrefKeyServerInfra, _serverInfraPath);
            _serverRoomRegistryPath = EditorPrefs.GetString(PrefKeyServerRoomReg, _serverRoomRegistryPath);
            _clientRoomRegistryPath = EditorPrefs.GetString(PrefKeyClientRoomReg, _clientRoomRegistryPath);

            // 全局模块
            _globalConfig.ModuleName = EditorPrefs.GetString(PrefKeyGlobalModuleName, _globalConfig.ModuleName);
            _globalConfig.ClientNamespace = EditorPrefs.GetString(PrefKeyGlobalClientNs, _globalConfig.ClientNamespace);
            _globalConfig.ServerNamespace = EditorPrefs.GetString(PrefKeyGlobalServerNs, _globalConfig.ServerNamespace);
            _globalConfig.ProtoNamespace = EditorPrefs.GetString(PrefKeyGlobalProtoNs, _globalConfig.ProtoNamespace);
            _globalConfig.ClientOutputPath =
                EditorPrefs.GetString(PrefKeyGlobalClientPath, _globalConfig.ClientOutputPath);
            _globalConfig.ServerOutputPath =
                EditorPrefs.GetString(PrefKeyGlobalServerPath, _globalConfig.ServerOutputPath);
            _globalConfig.ProtoOutputPath =
                EditorPrefs.GetString(PrefKeyGlobalProtoPath, _globalConfig.ProtoOutputPath);

            // 房间组件
            _roomConfig.ComponentName = EditorPrefs.GetString(PrefKeyRoomCompName, _roomConfig.ComponentName);
            _roomConfig.StableComponentId = EditorPrefs.GetString(PrefKeyRoomStableId, _roomConfig.StableComponentId);
            _roomConfig.ServerNamespace = EditorPrefs.GetString(PrefKeyRoomServerNs, _roomConfig.ServerNamespace);
            _roomConfig.ClientNamespace = EditorPrefs.GetString(PrefKeyRoomClientNs, _roomConfig.ClientNamespace);
            _roomConfig.ProtoNamespace = EditorPrefs.GetString(PrefKeyRoomProtoNs, _roomConfig.ProtoNamespace);
            _roomConfig.ServerOutputPath = EditorPrefs.GetString(PrefKeyRoomServerPath, _roomConfig.ServerOutputPath);
            _roomConfig.ClientOutputPath = EditorPrefs.GetString(PrefKeyRoomClientPath, _roomConfig.ClientOutputPath);
            _roomConfig.ProtoOutputPath = EditorPrefs.GetString(PrefKeyRoomProtoPath, _roomConfig.ProtoOutputPath);

            // 协议定义面板
            _protoOnlyFileName = EditorPrefs.GetString(PrefKeyProtoFileName, _protoOnlyFileName);
            _protoOnlyOutputPath = EditorPrefs.GetString(PrefKeyProtoOutputPath, _protoOnlyOutputPath);
            _protoOnlyNamespace = EditorPrefs.GetString(PrefKeyProtoNamespace, _protoOnlyNamespace);
            _protoOnlyModuleName = EditorPrefs.GetString(PrefKeyProtoModuleName, _protoOnlyModuleName);
        }
    }
}