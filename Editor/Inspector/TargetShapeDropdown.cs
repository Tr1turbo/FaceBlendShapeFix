using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.IMGUI.Controls;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    internal sealed class TargetShapeDropdown : AdvancedDropdown
    {
        private readonly List<Option> _options;
        private readonly Action<string> _onSelect;

        public TargetShapeDropdown(AdvancedDropdownState state, IEnumerable<Option> options, Action<string> onSelect)
            : base(state)
        {
            minimumSize = new Vector2(220f, 220f);
            _options = options?.ToList() ?? new List<Option>();
            _onSelect = onSelect;
        }

        public new void Show(Rect rect)
        {
            base.Show(rect);
            SetMaxHeightForOpenedPopup(rect, 880f);
        }
        
        
        //from https://discussions.unity.com/t/add-maximum-window-size-to-advanceddropdown-control/753671/3
        private static void SetMaxHeightForOpenedPopup(Rect buttonRect, float maxHeight)
        {
            var window = EditorWindow.focusedWindow;

            if(window == null)
            {
                Debug.LogWarning("EditorWindow.focusedWindow was null.");
                return;
            }

            if(!string.Equals(window.GetType().Namespace, typeof(AdvancedDropdown).Namespace))
            {
                Debug.LogWarning("EditorWindow.focusedWindow " + EditorWindow.focusedWindow.GetType().FullName + " was not in expected namespace.");
                return;
            }

            var position = window.position;
            if(position.height <= maxHeight)
            {
                return;
            }

            position.height = maxHeight;
            window.minSize = position.size;
            window.maxSize = position.size;
            window.position = position;
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(buttonRect), position.size);
        }
        
        
        
        // protected override Vector2 GetWindowSize(AdvancedDropdownItem item)
        // {
        //     const float MaxHeight = 340f;
        //     var size = base.GetWindowSize(item);
        //     size.y = Mathf.Min(size.y, MaxHeight);
        //     return size;
        // }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("BlendShapes");

            foreach (var option in _options)
            {
                var item = new TargetShapeDropdownItem(option.DisplayName, option.BlendShapeName, option.Enabled);
                item.enabled = option.Enabled;
                root.AddChild(item);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is TargetShapeDropdownItem selected && selected.Enabled)
            {
                _onSelect?.Invoke(selected.BlendShapeName);
            }
        }

        internal readonly struct Option
        {
            public string BlendShapeName { get; }
            public string DisplayName { get; }
            public bool Enabled { get; }

            public Option(string blendShapeName, string displayName, bool enabled = true)
            {
                BlendShapeName = blendShapeName;
                DisplayName = string.IsNullOrEmpty(displayName) ? blendShapeName : displayName;
                Enabled = enabled;
            }
        }

        private sealed class TargetShapeDropdownItem : AdvancedDropdownItem
        {
            public string BlendShapeName { get; }
            public bool Enabled { get; }

            public TargetShapeDropdownItem(string label, string blendShapeName, bool enabled) : base(label)
            {
                BlendShapeName = blendShapeName;
                Enabled = enabled;
            }
        }
    }
}
