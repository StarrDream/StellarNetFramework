using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 引入框架命名空间
using StellarNet.Server.Config;
using StellarNet.Client.Config;

namespace StellarNet.Editor.Tools
{
    public class StellarNetConfigurator : EditorWindow
    {
        // --- 1. 脚本路径 ---
        private const string ServerScriptPath = "Assets/StellarNetFramework/Runtime/Server/Config/NetConfig.cs";
        private const string ClientScriptPath = "Assets/StellarNetFramework/Runtime/Client/Config/ClientNetConfig.cs";

        private const string ServerGenCodePath = "Assets/Scripts/Server/Config/NetConfig_App.cs";
        private const string ClientGenCodePath = "Assets/Scripts/Client/Config/ClientNetConfig_App.cs";

        // --- 2. 文件夹名称约定 ---
        private const string ServerSubFolder = "ServerNetConfig";
        private const string ClientSubFolder = "ClientNetConfig";
        private const string ServerFileName = "NetConfig.json";
        private const string ClientFileName = "ClientNetConfig.json";

        // --- 3. 场景脚本绑定配置 ---
        private const string ServerInfrastructureType = "GlobalInfrastructure";
        private const string ClientInfrastructureType = "ClientInfrastructure";
        private const string TargetFieldName = "ConfigLoadPath";

        // --- 4. UI 风格 ---
        private static readonly Color ColContent = new Color(0.20f, 0.20f, 0.20f);
        private static readonly Color ColSectionBar = new Color(0.25f, 0.50f, 0.80f);
        private static readonly Color ColExtensionBar = new Color(0.30f, 0.60f, 0.40f);
        private static readonly Color ColDivider = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColFieldBgOdd = new Color(0.21f, 0.21f, 0.21f);
        private static readonly Color ColFieldBgEven = new Color(0.18f, 0.18f, 0.18f);

        private GUIStyle _styleHeader;
        private GUIStyle _styleSectionLabel;
        private GUIStyle _styleFieldLabel;
        private GUIStyle _stylePathLabel;
        private GUIStyle _styleStatusLabel;
        private bool _stylesReady;

        // --- 5. 数据结构 ---
        public enum ConfigMode
        {
            Server,
            Client
        }

        public enum PathMode
        {
            StreamingAssets,
            PersistentData,
            Custom
        }

        public enum FieldType
        {
            Int,
            Float,
            String,
            Bool
        }

        [Serializable]
        public class CoreFieldData
        {
            public string Name;
            public object Value;
            public Type SystemType;
            public string Comment;
        }

        [Serializable]
        public class ExtensionFieldData
        {
            public string Name = "NewField";
            public FieldType Type = FieldType.String;
            public string ValueStr = "";
            public string Comment = "";
        }

        [Serializable]
        private class SchemaWrapper
        {
            public List<ExtensionFieldData> Fields;
        }

        // --- 6. 运行时状态 ---
        private ConfigMode _mode = ConfigMode.Server;
        private PathMode _pathModeServer = PathMode.StreamingAssets;
        private PathMode _pathModeClient = PathMode.StreamingAssets;

        private string _customPathServer;
        private string _customPathClient;

        private List<CoreFieldData> _coreFields = new List<CoreFieldData>();
        private List<ExtensionFieldData> _extensionFields = new List<ExtensionFieldData>();
        private Vector2 _scrollPos;
        private bool _isLoaded = false;

        [MenuItem("StellarNet/全能配置控制台 (Configurator)")]
        public static void ShowWindow()
        {
            var w = GetWindow<StellarNetConfigurator>("配置控制台");
            w.minSize = new Vector2(650, 650);
        }

        private void OnEnable()
        {
            _pathModeServer = (PathMode)EditorPrefs.GetInt("StellarConfig_Mode_Server", (int)PathMode.StreamingAssets);
            _pathModeClient = (PathMode)EditorPrefs.GetInt("StellarConfig_Mode_Client", (int)PathMode.StreamingAssets);
            _customPathServer = EditorPrefs.GetString("StellarConfig_CustomPath_Server", "");
            _customPathClient = EditorPrefs.GetString("StellarConfig_CustomPath_Client", "");
        }

        private void OnGUI()
        {
            BuildStyles();

            float topHeight = 100f;
            float bottomHeight = 40f;
            float contentHeight = position.height - topHeight - bottomHeight;

            DrawTopArea(topHeight);

            if (!_isLoaded) LoadAll();

            EditorGUI.DrawRect(new Rect(0, topHeight, position.width, contentHeight), ColContent);

            GUILayout.BeginArea(new Rect(0, topHeight, position.width, contentHeight));
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            {
                GUILayout.Space(10);
                DrawCoreSection();
                GUILayout.Space(20);
                DrawExtensionSection();
                GUILayout.Space(20);
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(0, position.height - bottomHeight, position.width, bottomHeight));
            DrawActionBar();
            GUILayout.EndArea();
        }

        private void BuildStyles()
        {
            if (_stylesReady) return;
            _styleHeader = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 14, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            _styleSectionLabel = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 11, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } };
            _styleFieldLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }, alignment = TextAnchor.MiddleLeft
            };
            _stylePathLabel = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }, wordWrap = false };
            _styleStatusLabel = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = new Color(0.4f, 0.8f, 0.4f) }, alignment = TextAnchor.MiddleRight };
            _stylesReady = true;
        }

        private void DrawTopArea(float height)
        {
            GUILayout.BeginArea(new Rect(0, 0, position.width, height));

            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(30));
            GUILayout.Space(10);
            GUILayout.Label("StellarNet Configurator", _styleHeader, GUILayout.Width(200));
            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            string[] tabs = { "服务端 (Server)", "客户端 (Client)" };
            int newMode = GUILayout.Toolbar((int)_mode, tabs, GUILayout.Height(24), GUILayout.Width(300));
            if (EditorGUI.EndChangeCheck())
            {
                _mode = (ConfigMode)newMode;
                _isLoaded = false;
            }

            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();

            // Path Selector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(64));
            {
                GUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("配置文件加载源:", GUILayout.Width(100));

                PathMode currentPathMode = _mode == ConfigMode.Server ? _pathModeServer : _pathModeClient;
                EditorGUI.BeginChangeCheck();
                PathMode newPathMode = (PathMode)EditorGUILayout.EnumPopup(currentPathMode, GUILayout.Width(150));
                if (EditorGUI.EndChangeCheck())
                {
                    if (_mode == ConfigMode.Server)
                    {
                        _pathModeServer = newPathMode;
                        EditorPrefs.SetInt("StellarConfig_Mode_Server", (int)newPathMode);
                    }
                    else
                    {
                        _pathModeClient = newPathMode;
                        EditorPrefs.SetInt("StellarConfig_Mode_Client", (int)newPathMode);
                    }

                    _isLoaded = false;
                }

                if (newPathMode == PathMode.Custom)
                {
                    if (GUILayout.Button("选择目录...", GUILayout.Width(80)))
                    {
                        string currentCustom = _mode == ConfigMode.Server ? _customPathServer : _customPathClient;
                        string path = EditorUtility.OpenFolderPanel("选择 JSON 导出根目录", currentCustom, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (path.StartsWith(Application.dataPath))
                                path = "Assets" + path.Substring(Application.dataPath.Length);

                            if (_mode == ConfigMode.Server)
                            {
                                _customPathServer = path;
                                EditorPrefs.SetString("StellarConfig_CustomPath_Server", path);
                            }
                            else
                            {
                                _customPathClient = path;
                                EditorPrefs.SetString("StellarConfig_CustomPath_Client", path);
                            }

                            _isLoaded = false;
                        }
                    }
                }

                string targetScript = _mode == ConfigMode.Server ? ServerInfrastructureType : ClientInfrastructureType;
                var foundObj = GameObject.FindObjectOfType(Type.GetType(targetScript) ?? GetTypeByName(targetScript));
                if (foundObj != null)
                    GUILayout.Label($"已连接场景脚本: {targetScript}", _styleStatusLabel);
                else
                    GUILayout.Label($"场景中未找到: {targetScript}",
                        new GUIStyle(_styleStatusLabel) { normal = { textColor = Color.yellow } });

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(5);
                string fullPath = GetFullJsonPath();
                GUILayout.Label($"当前读写路径: {fullPath}", _stylePathLabel);
                GUILayout.Space(5);
            }
            EditorGUILayout.EndVertical();

            GUILayout.EndArea();
        }

        private void DrawCoreSection()
        {
            DrawSectionHeader("框架核心参数 (Core Framework)", ColSectionBar);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(5);

            for (int i = 0; i < _coreFields.Count; i++)
            {
                var field = _coreFields[i];
                Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(24));
                EditorGUI.DrawRect(rowRect, i % 2 == 0 ? ColFieldBgOdd : ColFieldBgEven);

                GUILayout.Space(10);

                EditorGUILayout.BeginVertical(GUILayout.Width(220));
                {
                    GUILayout.Space(4);
                    string tooltip = string.IsNullOrEmpty(field.Comment) ? "无注释" : field.Comment;
                    GUIContent labelContent = new GUIContent(field.Name, tooltip);
                    GUILayout.Label(labelContent, _styleFieldLabel);
                }
                EditorGUILayout.EndVertical();

                GUILayout.Space(10);

                if (field.SystemType == typeof(int))
                    field.Value = EditorGUILayout.IntField(Convert.ToInt32(field.Value));
                else if (field.SystemType == typeof(float))
                    field.Value = EditorGUILayout.FloatField(Convert.ToSingle(field.Value));
                else if (field.SystemType == typeof(string))
                    field.Value = EditorGUILayout.TextField(Convert.ToString(field.Value));
                else if (field.SystemType == typeof(bool))
                    field.Value = EditorGUILayout.Toggle(Convert.ToBoolean(field.Value));
                else
                    GUILayout.Label(field.Value?.ToString() ?? "null");

                GUILayout.Space(10);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawExtensionSection()
        {
            DrawSectionHeader("业务扩展参数 (Partial Extension)", ColExtensionBar);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("字段名", GUILayout.Width(150));
            GUILayout.Label("类型", GUILayout.Width(80));
            GUILayout.Label("值", GUILayout.ExpandWidth(true));
            GUILayout.Label("注释", GUILayout.Width(150));
            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                _extensionFields.Add(new ExtensionFieldData());
            }

            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < _extensionFields.Count; i++)
            {
                var field = _extensionFields[i];
                EditorGUILayout.BeginHorizontal(GUILayout.Height(22));

                field.Name = EditorGUILayout.TextField(field.Name, GUILayout.Width(150));
                field.Type = (FieldType)EditorGUILayout.EnumPopup(field.Type, GUILayout.Width(80));

                switch (field.Type)
                {
                    case FieldType.Int:
                        int.TryParse(field.ValueStr, out int iVal);
                        field.ValueStr = EditorGUILayout.IntField(iVal).ToString();
                        break;
                    case FieldType.Float:
                        float.TryParse(field.ValueStr, out float fVal);
                        field.ValueStr = EditorGUILayout.FloatField(fVal).ToString();
                        break;
                    case FieldType.Bool:
                        bool.TryParse(field.ValueStr, out bool bVal);
                        field.ValueStr = EditorGUILayout.Toggle(bVal).ToString();
                        break;
                    default:
                        field.ValueStr = EditorGUILayout.TextField(field.ValueStr);
                        break;
                }

                field.Comment = EditorGUILayout.TextField(field.Comment, GUILayout.Width(150));

                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    _extensionFields.RemoveAt(i);
                }

                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSectionHeader(string title, Color barColor)
        {
            Rect r = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(r.x, r.y + 20f, r.width, 1f), ColDivider);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 4f, 4f, 16f), barColor);
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, r.width, 20f), title, _styleSectionLabel);
        }

        private void DrawActionBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(40));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("重载 (Reload)", GUILayout.Height(30), GUILayout.Width(100)))
            {
                _isLoaded = false;
            }

            GUILayout.Space(10);

            if (GUILayout.Button("仅保存 JSON & 同步场景", GUILayout.Height(30), GUILayout.Width(180)))
            {
                string jsonPath = SaveJson();
                SaveSchema();
                SyncToSceneScript();
                AssetDatabase.Refresh();
                PingAndReveal(jsonPath, true);
            }

            GUILayout.Space(10);

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
            if (GUILayout.Button("生成代码 & 保存 & 同步", GUILayout.Height(30), GUILayout.Width(180)))
            {
                string codePath = GeneratePartialCode();
                string jsonPath = SaveJson();
                SaveSchema();
                SyncToSceneScript();
                AssetDatabase.Refresh();
                PingAndReveal(jsonPath, true);
            }

            GUI.backgroundColor = Color.white;

            GUILayout.Space(20);
            GUILayout.EndHorizontal();
        }

        // --- 核心逻辑 ---

        private string GetBaseDirectory()
        {
            PathMode mode = _mode == ConfigMode.Server ? _pathModeServer : _pathModeClient;
            string customPath = _mode == ConfigMode.Server ? _customPathServer : _customPathClient;

            switch (mode)
            {
                case PathMode.StreamingAssets:
                    return Application.streamingAssetsPath;
                case PathMode.PersistentData:
                    return Application.persistentDataPath;
                case PathMode.Custom:
                    return string.IsNullOrEmpty(customPath) ? Application.streamingAssetsPath : customPath;
                default:
                    return Application.streamingAssetsPath;
            }
        }

        private string GetFullJsonPath()
        {
            string baseDir = GetBaseDirectory();
            string subFolder = _mode == ConfigMode.Server ? ServerSubFolder : ClientSubFolder;
            string fileName = _mode == ConfigMode.Server ? ServerFileName : ClientFileName;
            return Path.Combine(baseDir, subFolder, fileName);
        }

        // [关键修改] 同步到场景时，使用标记路径（Token Path）而非绝对路径
        private void SyncToSceneScript()
        {
            string targetTypeName = _mode == ConfigMode.Server ? ServerInfrastructureType : ClientInfrastructureType;
            string subFolder = _mode == ConfigMode.Server ? ServerSubFolder : ClientSubFolder;
            PathMode mode = _mode == ConfigMode.Server ? _pathModeServer : _pathModeClient;

            Type targetType = Type.GetType(targetTypeName) ?? GetTypeByName(targetTypeName);
            if (targetType == null)
            {
                Debug.LogWarning($"[StellarConfig] 无法找到类型 {targetTypeName}，请确保类名正确且已编译。");
                return;
            }

            UnityEngine.Object foundObj = GameObject.FindObjectOfType(targetType);
            if (foundObj == null)
            {
                Debug.LogWarning($"[StellarConfig] 场景中未找到挂载了 {targetTypeName} 的物体，跳过 Inspector 赋值。");
                return;
            }

            // 构造标记路径
            string tokenizedPath = "";
            if (mode == PathMode.StreamingAssets)
            {
                // 格式: @StreamingAssets/ServerNetConfig
                tokenizedPath = $"@StreamingAssets/{subFolder}";
            }
            else if (mode == PathMode.PersistentData)
            {
                // 格式: @PersistentData/ServerNetConfig
                tokenizedPath = $"@PersistentData/{subFolder}";
            }
            else
            {
                // Custom 模式
                string customBase = _mode == ConfigMode.Server ? _customPathServer : _customPathClient;
                string fullCustomPath = Path.Combine(customBase, subFolder);

                // 尝试将 DataPath 转换为标记，实现跨环境兼容
                string dataPath = Application.dataPath.Replace("\\", "/");
                string normalizedCustom = fullCustomPath.Replace("\\", "/");

                if (normalizedCustom.StartsWith(dataPath))
                {
                    tokenizedPath = normalizedCustom.Replace(dataPath, "@DataPath");
                }
                else
                {
                    // 确实是外部绝对路径，只能保留原样
                    tokenizedPath = normalizedCustom;
                }
            }

            SerializedObject so = new SerializedObject(foundObj);
            SerializedProperty prop = so.FindProperty(TargetFieldName);

            if (prop != null && prop.propertyType == SerializedPropertyType.String)
            {
                prop.stringValue = tokenizedPath;
                so.ApplyModifiedProperties();
                Debug.Log(
                    $"[StellarConfig] 已将标记路径同步到 {foundObj.name}.{targetTypeName}.{TargetFieldName}:\n{tokenizedPath}");
            }
            else
            {
                Debug.LogError($"[StellarConfig] 在 {targetTypeName} 中未找到名为 '{TargetFieldName}' 的 string 字段。");
            }
        }

        private Type GetTypeByName(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name) return type;
                }
            }

            return null;
        }

        private void LoadAll()
        {
            _coreFields.Clear();
            _extensionFields.Clear();

            string jsonPath = GetFullJsonPath();
            string scriptPath = _mode == ConfigMode.Server ? ServerScriptPath : ClientScriptPath;
            Type configType = _mode == ConfigMode.Server ? typeof(NetConfig) : typeof(ClientNetConfig);

            var comments = ParseComments(scriptPath);

            JObject jsonObj = new JObject();
            if (File.Exists(jsonPath))
            {
                try
                {
                    jsonObj = JObject.Parse(File.ReadAllText(jsonPath));
                }
                catch
                {
                }
            }
            else
            {
                object defaultInstance = Activator.CreateInstance(configType);
                jsonObj = JObject.Parse(JsonConvert.SerializeObject(defaultInstance));
            }

            LoadSchema();
            HashSet<string> extNames = new HashSet<string>(_extensionFields.Select(e => e.Name));
            FieldInfo[] fields = configType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo[] props = configType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            var allMembers = new List<MemberInfo>();
            allMembers.AddRange(fields);
            allMembers.AddRange(props);

            foreach (var member in allMembers)
            {
                if (extNames.Contains(member.Name)) continue;

                Type memberType = null;
                object val = null;

                if (member is FieldInfo f)
                {
                    memberType = f.FieldType;
                    val = f.GetValue(Activator.CreateInstance(configType));
                }
                else if (member is PropertyInfo p)
                {
                    if (!p.CanWrite) continue;
                    memberType = p.PropertyType;
                    try
                    {
                        val = p.GetValue(Activator.CreateInstance(configType));
                    }
                    catch
                    {
                    }
                }

                if (jsonObj.TryGetValue(member.Name, out JToken token))
                {
                    try
                    {
                        val = token.ToObject(memberType);
                    }
                    catch
                    {
                    }
                }

                string comment = "";
                comments.TryGetValue(member.Name, out comment);

                _coreFields.Add(new CoreFieldData
                {
                    Name = member.Name,
                    SystemType = memberType,
                    Value = val,
                    Comment = comment
                });
            }

            foreach (var ext in _extensionFields)
            {
                if (jsonObj.TryGetValue(ext.Name, out JToken token))
                {
                    ext.ValueStr = token.ToString();
                }
            }

            _isLoaded = true;
        }

        private string SaveJson()
        {
            JObject root = new JObject();

            foreach (var core in _coreFields)
            {
                if (core.SystemType == typeof(int)) root[core.Name] = Convert.ToInt32(core.Value);
                else if (core.SystemType == typeof(float)) root[core.Name] = Convert.ToSingle(core.Value);
                else if (core.SystemType == typeof(bool)) root[core.Name] = Convert.ToBoolean(core.Value);
                else root[core.Name] = core.Value.ToString();
            }

            foreach (var ext in _extensionFields)
            {
                switch (ext.Type)
                {
                    case FieldType.Int:
                        int.TryParse(ext.ValueStr, out int i);
                        root[ext.Name] = i;
                        break;
                    case FieldType.Float:
                        float.TryParse(ext.ValueStr, out float f);
                        root[ext.Name] = f;
                        break;
                    case FieldType.Bool:
                        bool.TryParse(ext.ValueStr, out bool b);
                        root[ext.Name] = b;
                        break;
                    default:
                        root[ext.Name] = ext.ValueStr;
                        break;
                }
            }

            string fullPath = GetFullJsonPath();
            EnsureDirectory(fullPath);
            File.WriteAllText(fullPath, root.ToString(Formatting.Indented), Encoding.UTF8);
            return fullPath;
        }

        private string GeneratePartialCode()
        {
            string namespaceName = _mode == ConfigMode.Server ? "StellarNet.Server.Config" : "StellarNet.Client.Config";
            string className = _mode == ConfigMode.Server ? "NetConfig" : "ClientNetConfig";
            string path = _mode == ConfigMode.Server ? ServerGenCodePath : ClientGenCodePath;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// <auto-generated> 由 StellarNetConfigurator 生成 </auto-generated>");
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public partial class {className}");
            sb.AppendLine("    {");

            foreach (var field in _extensionFields)
            {
                if (!string.IsNullOrEmpty(field.Comment))
                    sb.AppendLine($"        /// <summary> {field.Comment} </summary>");

                string typeStr = field.Type.ToString().ToLower();
                if (field.Type == FieldType.String) typeStr = "string";

                sb.AppendLine($"        public {typeStr} {field.Name};");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            EnsureDirectory(path);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private Dictionary<string, string> ParseComments(string scriptPath)
        {
            var result = new Dictionary<string, string>();
            if (!File.Exists(scriptPath)) return result;

            string[] lines = File.ReadAllLines(scriptPath);
            StringBuilder currentComment = new StringBuilder();
            Regex fieldRegex =
                new Regex(@"public\s+(?:static\s+|readonly\s+|const\s+)*[\w\<\>\[\]\?]+\s+(\w+)\s*(?:\{|;|\||=)");

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("///"))
                {
                    string content = line.Replace("///", "").Replace("<summary>", "").Replace("</summary>", "").Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (currentComment.Length > 0) currentComment.Append(" ");
                        currentComment.Append(content);
                    }

                    continue;
                }

                if (line.StartsWith("[")) continue;

                if (line.StartsWith("public"))
                {
                    Match match = fieldRegex.Match(line);
                    if (match.Success)
                    {
                        string fieldName = match.Groups[1].Value;
                        if (currentComment.Length > 0)
                        {
                            result[fieldName] = currentComment.ToString();
                            currentComment.Clear();
                        }
                    }
                    else currentComment.Clear();
                }
                else currentComment.Clear();
            }

            return result;
        }

        private void PingAndReveal(string path, bool revealInFinder)
        {
            Debug.Log($"[StellarConfig] 成功保存至: {path}");
            if (path.StartsWith("Assets"))
            {
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }

            if (revealInFinder) EditorUtility.RevealInFinder(path);
        }

        private void SaveSchema()
        {
            string key = _mode == ConfigMode.Server ? "StellarSchema_Server" : "StellarSchema_Client";
            var wrapper = new SchemaWrapper { Fields = _extensionFields };
            EditorPrefs.SetString(key, JsonUtility.ToJson(wrapper));
        }

        private void LoadSchema()
        {
            string key = _mode == ConfigMode.Server ? "StellarSchema_Server" : "StellarSchema_Client";
            string json = EditorPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(json))
            {
                var wrapper = JsonUtility.FromJson<SchemaWrapper>(json);
                if (wrapper != null) _extensionFields = wrapper.Fields ?? new List<ExtensionFieldData>();
            }
        }

        private void EnsureDirectory(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}