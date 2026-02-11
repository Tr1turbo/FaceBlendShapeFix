using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix.Inspector
{
    public class BlendShapeActivationObserver: IDisposable
    {
        public struct BlendShapeChange
        {
            public IReadOnlyCollection<string> Added;
            public IReadOnlyCollection<string> Removed;
            public IReadOnlyCollection<string> Active;
        }
        
        public event Action<BlendShapeChange> OnActiveBlendShapesChanged;
        
        private SkinnedMeshRenderer _renderer;

        public SkinnedMeshRenderer Renderer
        {
            get => _renderer;
            set
            {
                _renderer = value;
                Refresh();
            }
        }
        
        private readonly HashSet<string> _previousActive = new HashSet<string>();
        private readonly HashSet<string> _currentActive = new HashSet<string>();
        private readonly Dictionary<string, float> _currentWeights = new ();
        
        // Read-only view of current active shapes
        public IReadOnlyCollection<string> ActiveShapes => _currentActive;
        
        public float GetWeight(string blendShapeName)
        {
            if(_currentWeights == null) return 0;
            return _currentWeights.GetValueOrDefault(blendShapeName, 0);
        }
        
        public BlendShapeActivationObserver(SkinnedMeshRenderer renderer)
        {
            _renderer = renderer;
            Undo.postprocessModifications += OnPostprocessModifications;
            Refresh();
        }
                
        public void Dispose()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
        }
        
        
        private UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (var mod in modifications)
            {
                var prop = mod.currentValue;

                if (prop == null || prop.target == null)
                    continue;

                if (!prop.propertyPath.StartsWith("m_BlendShapeWeights"))
                    continue;

                if (prop.target is not SkinnedMeshRenderer renderer)
                    continue;

                if (renderer != _renderer)
                    continue;

                Refresh();
                break;
            }

            return modifications;
        }
        
        /// <summary>
        /// Refresh active shapes by reading blendshape weights.
        /// </summary>
        public void Refresh()
        {
            if (_renderer == null || _renderer.sharedMesh == null)
                return;
            
            UpdateCurrentActive();
            
            if (!HasChanged())
                return;
            
            var change = ComputeChange();
            
            OnActiveBlendShapesChanged?.Invoke(change);
        }
        
        private void UpdateCurrentActive()
        {
            _currentWeights.Clear();
            _currentActive.Clear();
            
            var mesh = _renderer.sharedMesh;
            int count = mesh.blendShapeCount;

            for (int i = 0; i < count; i++)
            {
                float weight = _renderer.GetBlendShapeWeight(i);
                if (weight <= 0f)
                    continue;
              
                string name = mesh.GetBlendShapeName(i);
                _currentWeights.Add(name, weight);
                _currentActive.Add(name);
            }
        }
        
        
        private bool HasChanged()
        {
            if (_currentActive.Count != _previousActive.Count)
                return true;

            foreach (var s in _currentActive)
            {
                if (!_previousActive.Contains(s))
                    return true;
            }

            return false;
        }
        
        private BlendShapeChange ComputeChange()
        {
            // Added
            var added = new List<string>();
            foreach (var s in _currentActive)
            {
                if (!_previousActive.Contains(s))
                    added.Add(s);
            }

            // Removed
            var removed = new List<string>();
            foreach (var s in _previousActive)
            {
                if (!_currentActive.Contains(s))
                    removed.Add(s);
            }

            var change =  new BlendShapeChange
            {
                Added = added,
                Removed = removed,
                Active = _currentActive
            };
            
            _previousActive.Clear();
            foreach (var s in _currentActive)
                _previousActive.Add(s);
            return change;
        }
    }
}