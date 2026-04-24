using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public event Action OnBlendShapeWeightsEdited;
        
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

                OnBlendShapeWeightsEdited?.Invoke();
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
            Stopwatch stopwatch = Stopwatch.StartNew();
            int blendShapeCount = _renderer != null && _renderer.sharedMesh != null
                ? _renderer.sharedMesh.blendShapeCount
                : 0;

            if (_renderer == null || _renderer.sharedMesh == null)
            {
                ClearCurrentActive();
                NotifyIfChanged();
                stopwatch.Stop();
                FaceBlendShapeFixDiagnostics.LogIfSlow(
                    "Activation observer refresh",
                    stopwatch.Elapsed.TotalMilliseconds,
                    $"renderer=null, blendShapes={blendShapeCount}, active=0",
                    thresholdMilliseconds: 4d);
                return;
            }

            UpdateCurrentActive();
            NotifyIfChanged();
            stopwatch.Stop();
            FaceBlendShapeFixDiagnostics.LogIfSlow(
                "Activation observer refresh",
                stopwatch.Elapsed.TotalMilliseconds,
                $"renderer={_renderer.name}, blendShapes={blendShapeCount}, active={_currentActive.Count}",
                thresholdMilliseconds: 4d);
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

        private void ClearCurrentActive()
        {
            _currentWeights.Clear();
            _currentActive.Clear();
        }

        private void NotifyIfChanged()
        {
            if (!HasChanged())
            {
                return;
            }

            BlendShapeChange change = ComputeChange();
            OnActiveBlendShapesChanged?.Invoke(change);
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
