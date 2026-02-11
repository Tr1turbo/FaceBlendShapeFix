using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix
{
    /// <summary>
    /// Localization system that reads JSON files for multi-language support in the Unity Editor.
    /// </summary>
    public static class Localization
    {
        private const string EditorPrefKey = "Triturbo.FaceBlendShapeFix.Language";
        private const string DefaultLanguage = "en";
        private const string LocalesFolderName = "Locales";

        private static Dictionary<string, string> _currentStrings = new();
        private static readonly Dictionary<string, Dictionary<string, string>> _languageCache = new();
        private static string _currentLanguage;
        private static string[] _availableLanguages;
        private static readonly List<string> _availableLanguageDisplayNames = new();
        private static string _localesPath;

        private const string LanguageNameKey = "language.name";

        /// <summary>
        /// Event fired when the language changes.
        /// </summary>
        public static event Action OnLanguageChanged;

        /// <summary>
        /// Gets the currently selected language code.
        /// </summary>
        public static string CurrentLanguage
        {
            get
            {
                if (string.IsNullOrEmpty(_currentLanguage))
                {
                    _currentLanguage = EditorPrefs.GetString(EditorPrefKey, DefaultLanguage);
                }
                return _currentLanguage;
            }
        }

        /// <summary>
        /// Gets all available language codes based on JSON files in the Locales folder.
        /// </summary>
        public static string[] AvailableLanguages
        {
            get
            {
                if (_availableLanguages == null)
                {
                    RefreshAvailableLanguages();
                }
                return _availableLanguages ?? Array.Empty<string>();
            }
        }

        /// <summary>
        /// Initializes the localization system. Call this during editor initialization.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            _localesPath = FindLocalesPath();
            RefreshAvailableLanguages();
            LoadLanguage(CurrentLanguage);
        }

        /// <summary>
        /// Gets a localized string for the given key.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <returns>The localized string, or the key itself if not found.</returns>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            EnsureLoaded();

            if (_currentStrings.TryGetValue(key, out string value))
                return value;

            return key;
        }

        /// <summary>
        /// Gets a localized string for the given key, or a default value if not found.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <param name="defaultValue">The default value if key is not found.</param>
        /// <returns>The localized string, or the default value if not found.</returns>
        public static string Get(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(key))
                return defaultValue ?? string.Empty;

            EnsureLoaded();

            if (_currentStrings.TryGetValue(key, out string value))
                return value;

            return defaultValue;
        }

        /// <summary>
        /// Gets a localized string with format arguments.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>The formatted localized string.</returns>
        public static string GetFormat(string key, params object[] args)
        {
            string format = Get(key);
            if (args == null || args.Length == 0)
                return format;

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        /// Gets a GUIContent with localized text and optional tooltip.
        /// Tooltip is looked up using key + ".tooltip".
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <returns>GUIContent with localized text and tooltip if available.</returns>
        public static GUIContent G(string key)
        {
            string text = Get(key);
            string tooltip = Get(key + ".tooltip", null);
            return tooltip != null ? new GUIContent(text, tooltip) : new GUIContent(text);
        }

        /// <summary>
        /// Gets a GUIContent with formatted localized text and optional tooltip.
        /// Tooltip is looked up using key + ".tooltip" and also formatted.
        /// </summary>
        /// <param name="key">The localization key.</param>
        /// <param name="args">Format arguments.</param>
        /// <returns>GUIContent with formatted localized text and tooltip if available.</returns>
        public static GUIContent GF(string key, params object[] args)
        {
            string text = GetFormat(key, args);
            string tooltipKey = key + ".tooltip";
            string tooltip = Get(tooltipKey, null);

            if (tooltip == null)
                return new GUIContent(text);

            try
            {
                return new GUIContent(text, string.Format(tooltip, args));
            }
            catch (FormatException)
            {
                return new GUIContent(text, tooltip);
            }
        }

        /// <summary>
        /// Changes the current language.
        /// </summary>
        /// <param name="languageCode">The language code (e.g., "en", "ja").</param>
        public static void SetLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                languageCode = DefaultLanguage;

            if (string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
                return;

            _currentLanguage = languageCode;
            EditorPrefs.SetString(EditorPrefKey, languageCode);
            LoadLanguage(languageCode);
            OnLanguageChanged?.Invoke();
        }

        /// <summary>
        /// Draws a language selection popup in the editor GUI.
        /// </summary>
        /// <param name="label">The label for the popup.</param>
        /// <returns>True if the language was changed.</returns>
        public static bool DrawLanguagePopup(string label = null)
        {
            string[] languages = AvailableLanguages;
            if (languages.Length == 0)
            {
                EditorGUILayout.HelpBox("No locale files found.", MessageType.Warning);
                return false;
            }

            int currentIndex = Array.IndexOf(languages, CurrentLanguage);
            if (currentIndex < 0)
                currentIndex = 0;

            string[] displayNames = _availableLanguageDisplayNames.ToArray();

            EditorGUI.BeginChangeCheck();
            int newIndex = string.IsNullOrEmpty(label)
                ? EditorGUILayout.Popup(currentIndex, displayNames)
                : EditorGUILayout.Popup(label, currentIndex, displayNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex && newIndex >= 0 && newIndex < languages.Length)
            {
                SetLanguage(languages[newIndex]);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Draws a language selection popup with a GUIContent label.
        /// </summary>
        /// <param name="label">The GUIContent label for the popup.</param>
        /// <returns>True if the language was changed.</returns>
        public static bool DrawLanguagePopup(GUIContent label)
        {
            string[] languages = AvailableLanguages;
            if (languages.Length == 0)
            {
                EditorGUILayout.HelpBox("No locale files found.", MessageType.Warning);
                return false;
            }

            int currentIndex = Array.IndexOf(languages, CurrentLanguage);
            if (currentIndex < 0)
                currentIndex = 0;

            string[] displayNames = _availableLanguageDisplayNames.ToArray();

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(label, currentIndex, displayNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex && newIndex >= 0 && newIndex < languages.Length)
            {
                SetLanguage(languages[newIndex]);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reloads all locale files from disk.
        /// </summary>
        public static void Reload()
        {
            _languageCache.Clear();
            RefreshAvailableLanguages();
            LoadLanguage(CurrentLanguage);
        }

        /// <summary>
        /// Clears the language cache, forcing a reload on next access.
        /// </summary>
        public static void ClearCache()
        {
            _languageCache.Clear();
            _currentStrings.Clear();
        }

        private static void EnsureLoaded()
        {
            if (_currentStrings.Count == 0)
            {
                LoadLanguage(CurrentLanguage);
            }
        }

        private static void LoadLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
                languageCode = DefaultLanguage;

            if (_languageCache.TryGetValue(languageCode, out var cached))
            {
                _currentStrings = cached;
                return;
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);

            string filePath = GetLocaleFilePath(languageCode);
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    ParseJson(json, strings);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Localization] Failed to load locale file '{filePath}': {e.Message}");
                }
            }
            else if (!string.Equals(languageCode, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                string fallbackPath = GetLocaleFilePath(DefaultLanguage);
                if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
                {
                    try
                    {
                        string json = File.ReadAllText(fallbackPath);
                        ParseJson(json, strings);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Localization] Failed to load fallback locale file '{fallbackPath}': {e.Message}");
                    }
                }
            }

            _languageCache[languageCode] = strings;
            _currentStrings = strings;
        }

        private static void ParseJson(string json, Dictionary<string, string> output)
        {
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var wrapper = JsonUtility.FromJson<LocaleWrapper>("{\"entries\":" + json + "}");
                // JsonUtility doesn't support Dictionary directly, so we use a simple parser
            }
            catch
            {
                // Fall through to manual parsing
            }

            // Simple JSON parsing for flat key-value structure
            ParseFlatJson(json, output);
        }

        private static void ParseFlatJson(string json, Dictionary<string, string> output)
        {
            if (string.IsNullOrEmpty(json))
                return;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return;

            json = json[1..^1].Trim();

            int i = 0;
            while (i < json.Length)
            {
                // Skip whitespace
                while (i < json.Length && char.IsWhiteSpace(json[i]))
                    i++;

                if (i >= json.Length)
                    break;

                // Parse key
                string key = ParseJsonString(json, ref i);
                if (key == null)
                    break;

                // Skip whitespace and colon
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ':'))
                    i++;

                if (i >= json.Length)
                    break;

                // Parse value
                string value = ParseJsonString(json, ref i);
                if (value != null)
                {
                    output[key] = value;
                }

                // Skip whitespace and comma
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ','))
                    i++;
            }
        }

        private static string ParseJsonString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"')
                return null;

            index++; // Skip opening quote

            var result = new System.Text.StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '\\' && index + 1 < json.Length)
                {
                    index++;
                    char escaped = json[index];
                    switch (escaped)
                    {
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case 'u' when index + 4 < json.Length:
                            string hex = json.Substring(index + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int unicode))
                            {
                                result.Append((char)unicode);
                                index += 4;
                            }
                            break;
                        default:
                            result.Append(escaped);
                            break;
                    }
                }
                else if (c == '"')
                {
                    index++; // Skip closing quote
                    return result.ToString();
                }
                else
                {
                    result.Append(c);
                }

                index++;
            }

            return null; // Unterminated string
        }

        private static string GetLocaleFilePath(string languageCode)
        {
            if (string.IsNullOrEmpty(_localesPath))
            {
                _localesPath = FindLocalesPath();
            }

            if (string.IsNullOrEmpty(_localesPath))
                return null;

            return Path.Combine(_localesPath, $"{languageCode}.json");
        }

        private static string FindLocalesPath()
        {
            // Find the Locales folder relative to this script
            string[] guids = AssetDatabase.FindAssets("t:Folder " + LocalesFolderName);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("com.triturbo.face-blendshape-fix") && path.EndsWith(LocalesFolderName))
                {
                    return Path.GetFullPath(path);
                }
            }

            // Fallback: look relative to the package
            string packagePath = "Packages/com.triturbo.face-blendshape-fix/Editor/" + LocalesFolderName;
            if (AssetDatabase.IsValidFolder(packagePath))
            {
                return Path.GetFullPath(packagePath);
            }

            return null;
        }

        private static void RefreshAvailableLanguages()
        {
            var languages = new List<string>();

            if (string.IsNullOrEmpty(_localesPath))
            {
                _localesPath = FindLocalesPath();
            }

            if (!string.IsNullOrEmpty(_localesPath) && Directory.Exists(_localesPath))
            {
                string[] files = Directory.GetFiles(_localesPath, "*.json");
                foreach (string file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        languages.Add(fileName);
                    }
                }
            }

            languages.Sort(StringComparer.OrdinalIgnoreCase);
            _availableLanguages = languages.ToArray();

            // Build display names list by reading language.name from each JSON file
            _availableLanguageDisplayNames.Clear();
            foreach (string code in _availableLanguages)
            {
                string displayName = GetLanguageDisplayName(code);
                _availableLanguageDisplayNames.Add(displayName ?? code);
            }
        }

        /// <summary>
        /// Reads the language.name key from a locale file to get its display name.
        /// </summary>
        private static string GetLanguageDisplayName(string languageCode)
        {
            // First check the cache
            if (_languageCache.TryGetValue(languageCode, out var cached))
            {
                if (cached.TryGetValue(LanguageNameKey, out string name))
                {
                    return name;
                }
            }

            // Read directly from file
            string filePath = GetLocaleFilePath(languageCode);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var strings = new Dictionary<string, string>(StringComparer.Ordinal);
                ParseFlatJson(json, strings);

                // Cache this for later use
                _languageCache[languageCode] = strings;

                if (strings.TryGetValue(LanguageNameKey, out string displayName))
                {
                    return displayName;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Localization] Failed to read display name from '{filePath}': {e.Message}");
            }

            return null;
        }

        public static T LocalizedEnumPopup<T>(GUIContent label, T enumValue, string enumKey) where T : Enum
        {
            // Localize enum display names
            string[] names = Enum.GetNames(typeof(T));
            GUIContent[] displayedOptions = names
                .Select(name => G($"{enumKey}.{name.Replace(" ", "_").ToLower()}"))
                .ToArray();

            int currentIndex = Array.IndexOf(names, enumValue.ToString());
            int newIndex = EditorGUILayout.Popup(label, currentIndex, displayedOptions);

            if (newIndex >= 0 && newIndex < names.Length)
            {
                return (T)Enum.Parse(typeof(T), names[newIndex]);
            }

            return enumValue;
        }
        public static void LocalizedEnumPropertyField(Rect rect, SerializedProperty property, GUIContent label, string enumKey)
        {
            if (property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUI.LabelField(rect, label.text, "Not an enum");
                return;
            }
            label = EditorGUI.BeginProperty(rect, label, property);
            
            
            GUIContent[] displayedOptions = property.enumDisplayNames
                .Select(name => G($"{enumKey}.{name.Replace(" ","_").ToLower()}"))
                .ToArray();
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(rect, label, property.enumValueIndex, displayedOptions);
            if (EditorGUI.EndChangeCheck())
            {
                if (newIndex != property.enumValueIndex)
                {
                    property.enumValueIndex = newIndex;
                }
            }
            EditorGUI.EndProperty();
        }
        

        [Serializable]
        private class LocaleWrapper
        {
            public LocaleEntry[] entries;
        }

        [Serializable]
        private class LocaleEntry
        {
            public string key;
            public string value;
        }
    }
}
