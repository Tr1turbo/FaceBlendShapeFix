using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    internal class EditorInputDialog : EditorWindow
    {
        private string _inputText = "";
        private string _message = "";
        private bool _shouldClose;
        private bool _confirmed;
        private bool _initialized;

        private static string _result;

        public static string Show(string title, string message, string defaultValue)
        {
            _result = null;

            var window = CreateInstance<EditorInputDialog>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._inputText = defaultValue ?? "";
            window._shouldClose = false;
            window._confirmed = false;
            window._initialized = false;

            Vector2 size = new Vector2(300, 100);
            window.minSize = size;
            window.maxSize = size;

            // Center the window on the main Unity editor window
            // var mainWindowPos = EditorGUIUtility.GetMainWindowPosition();
            // float x = mainWindowPos.x + (mainWindowPos.width - size.x) * 0.5f;
            // float y = mainWindowPos.y + (mainWindowPos.height - size.y) * 0.5f;
            // window.position = new Rect(x, y, size.x, size.y);

            window.ShowModal();


            return _result;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(5);

            GUI.SetNextControlName("InputField");
            _inputText = EditorGUILayout.TextField(_inputText);

            if (!_initialized)
            {
                EditorGUI.FocusTextInControl("InputField");
                _initialized = true;
            }

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button(Localization.Get("editor.cancel"), GUILayout.Width(80)))
                {
                    _shouldClose = true;
                    _confirmed = false;
                }

                if (GUILayout.Button(Localization.Get("editor.ok"), GUILayout.Width(80)))
                {
                    _shouldClose = true;
                    _confirmed = true;
                }
            }

            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    _shouldClose = true;
                    _confirmed = true;
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    _shouldClose = true;
                    _confirmed = false;
                    Event.current.Use();
                }
            }

            if (_shouldClose)
            {
                _result = _confirmed ? _inputText : null;
                Close();
            }
        }
    }
}
