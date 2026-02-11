#if FBF_NDMF
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;


namespace Triturbo.FaceBlendShapeFix
{
    internal sealed class FaceBlendShapeFixPreviewFilter : IRenderFilter
    {
        #region Fields

        private static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "Face BlendShape Fix Preview",
            "triturbo.shapeblend/preview",
            true
        );

        #endregion

        #region IRenderFilter Implementation

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(EnableNode.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            var seen = new HashSet<Renderer>();

            foreach (var component in context.GetComponentsByType<FaceBlendShapeFixComponent>())
            {
                if (component == null) continue;
                var isActive = context.Observe(component, c => c.isActiveAndEnabled);
                if (!isActive) continue;

                var renderer = context.Observe(component, c => c.TargetRenderer);
                if (renderer == null) continue;

                var mesh = context.Observe(renderer, r => r.sharedMesh);
                if (mesh == null) continue;
                if (!seen.Add(renderer))
                {
                    Debug.LogWarning($"ShapeBlend preview: renderer '{renderer.name}' already handled, skipping duplicate on '{component.name}'.");
                    continue;
                }
                groups.Add(RenderGroup.For(renderer).WithData(component));
            }
            return groups.ToImmutable();
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            Debug.Log("ShapeBlendPreviewFilter Instantiate called");
            var component = group.GetData<FaceBlendShapeFixComponent>();
            var source = component?.TargetRenderer;

            if (component == null || source == null)
            {
                return Task.FromResult<IRenderFilterNode>(null);
            }

            

            var node = new ShapeBlendPreviewNode(component, source);
            return node.Refresh(proxyPairs, context, RenderAspects.Everything);
        }

        #endregion
    }
    
    internal sealed class ShapeBlendPreviewNode : IRenderFilterNode
    {
        private struct PreviewMeshData
        {
            public Mesh Mesh;
            public HashSet<string> InverseShapes;
            public HashSet<string> AdditiveShapes;
        }


        private readonly FaceBlendShapeFixComponent _component;
        private readonly SkinnedMeshRenderer _sourceRenderer;
        private PreviewMeshData _previewData;

        private float _smoothWidth;

        private readonly List<(TargetShape targetShape, float weight)> _activeTargets = new();



        public RenderAspects WhatChanged => RenderAspects.Mesh | RenderAspects.Shapes;


        #region Constructor and Lifecycle

        public ShapeBlendPreviewNode(FaceBlendShapeFixComponent component, SkinnedMeshRenderer sourceRenderer)
        {
            _component = component;
            _sourceRenderer = sourceRenderer;
        }

        public void Dispose()
        {
            if (_previewData.Mesh != null)
            {
                Object.DestroyImmediate(_previewData.Mesh);
                _previewData = default;
            }
        }

        #endregion

        #region IRenderFilterNode Implementation

        public Task<IRenderFilterNode> Refresh(IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context,
            RenderAspects updatedAspects)
        {

            bool needsRebuild = false;

            var neededInvShapes = _previewData.InverseShapes;
            // Check if we need inv shapes that don't exist on the preview mesh
            if ((updatedAspects & RenderAspects.Shapes) != 0)
            {
                neededInvShapes = ComputeNeededInvShapes(_component, _sourceRenderer);
                if (_previewData.InverseShapes == null || neededInvShapes.Any(s => !_previewData.InverseShapes.Contains(s)))
                {
                    needsRebuild = true;
                }
            }

            // Check if we need add shapes that don't exist on the preview mesh
            var neededAddShapes = context.Observe(_component, ComputeNeededAddShapes);
            if (_previewData.AdditiveShapes == null || neededAddShapes.Any(s => !_previewData.AdditiveShapes.Contains(s)))
            {
                needsRebuild = true;
            }

            // Check if smooth width changed
            float currentSmoothWidth = context.Observe(_component, c => c.m_SmoothWidth);
            if (Math.Abs(currentSmoothWidth - _smoothWidth) > float.Epsilon)
            {
                _smoothWidth = currentSmoothWidth;
                needsRebuild = true;
            }

            
            // Rebuild only when activation hash changed, smooth width changed, or upstream mesh changed
            if (needsRebuild || (updatedAspects & RenderAspects.Mesh) != 0 || context.IsInvalidated)
            {
                RebuildPreviewMesh(neededInvShapes, neededAddShapes);
            }

            return Task.FromResult<IRenderFilterNode>(this);
        }

        public void OnFrame(Renderer original, Renderer proxy)
        {
            if (original is not SkinnedMeshRenderer source || proxy is not SkinnedMeshRenderer proxySmr)
            {
                return;
            }

            if (_previewData.Mesh != null)
            {
                proxySmr.sharedMesh = _previewData.Mesh;
            }

            Dictionary<int, float> targetWeights;

            if (_component.TryGetPreviewRequest(out var request) && request.Target != null)
            {
                targetWeights = CalculateProxyWeights(_sourceRenderer, proxySmr, request.Target.m_TargetShapeName);
                UpdateWeights(targetWeights, proxySmr, request.Target, request.Weight);
            }
            else
            {
                targetWeights = CalculateProxyWeights(_sourceRenderer, proxySmr);
            }

            foreach (var weight in targetWeights)
            {
                proxySmr.SetBlendShapeWeight(weight.Key, weight.Value);
            }

            SceneView.RepaintAll();
        }

        #endregion

        #region Weight Calculation

        private void UpdateWeights(Dictionary<int, float> weights, SkinnedMeshRenderer proxy, TargetShape current, float weight)
        {
            Mesh mesh = proxy.sharedMesh;
            int targetIndex = mesh.GetBlendShapeIndex(current.m_TargetShapeName);
            weights[targetIndex] = weight * current.m_Weight;
            
            var blendData = current.GetBlendData(_component.m_BlendShapeDefinitions);
            foreach (var data in blendData)
            {
                if (data == null) continue;
                int baseIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName);
                if (baseIndex < 0) continue;
                float baseWeight = proxy.GetBlendShapeWeight(baseIndex);
                
                int rightIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName + ".inv.R");
                int leftIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName + ".inv.L");
                if (rightIndex == -1 || leftIndex == -1)
                {
                    continue;
                };

                float factor = weight / 100f;
                float reducedLeft = baseWeight * data.m_LeftWeight * factor;
                float reducedRight = baseWeight * data.m_RightWeight * factor;
                    
                if (!weights.TryAdd(rightIndex, reducedRight))
                {
                    weights[rightIndex] = reducedRight;
                }
                if (!weights.TryAdd(leftIndex, reducedLeft))
                {
                    weights[leftIndex] = reducedLeft;
                }
                
            }

            if (ApplyAdditiveBlendData(weights, proxy, current.m_AdditiveBlendData, weight))
            {
                // InvalidatePreviewMesh();
                // UpdateAdditionalBlendShapeHash();
                // RebuildPreviewMesh();
                // if (_previewMesh != null)
                // {
                //     proxy.sharedMesh = _previewMesh;
                // }
            }
        }

        private static bool ApplyAdditiveBlendData(
            Dictionary<int, float> weights,
            SkinnedMeshRenderer proxy,
            IEnumerable<BlendData> additionalBlendData,
            float intensity)
        {
            if (additionalBlendData == null)
            {
                return false;
            }

            Mesh mesh = proxy.sharedMesh;
            bool missingBlendShape = false;

            foreach (var data in additionalBlendData)
            {
                if (data == null)
                {
                    continue;
                }
                float leftWeight = data.m_SplitLeftRight ? data.m_LeftWeight : data.m_Weight;
                float rightWeight = data.m_SplitLeftRight ? data.m_RightWeight : data.m_Weight;

                int rightIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName + ".add.R");
                int leftIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName + ".add.L");
                if (rightIndex == -1 || leftIndex == -1)
                {
                    //if no original blendshape found, m_TargetShapeName is invalid and ignore missing
                    missingBlendShape = mesh.GetBlendShapeIndex(data.m_TargetShapeName) != -1;
                    continue;
                }

                float additiveLeft = leftWeight * intensity;
                float additiveRight = rightWeight * intensity;

                if (!weights.TryAdd(rightIndex, additiveRight))
                {
                    weights[rightIndex] += additiveRight;
                }
                if (!weights.TryAdd(leftIndex, additiveLeft))
                {
                    weights[leftIndex] += additiveLeft;
                }
            }

            return missingBlendShape;
        }
        
        private Dictionary<int, float> CalculateProxyWeights(SkinnedMeshRenderer source, SkinnedMeshRenderer proxy, string skipShapeName = null)
        {
            Dictionary<int, float> weights = new Dictionary<int, float>();

            _activeTargets.Clear();

            var mesh = source.sharedMesh;
            if (mesh == null)
            {
                return weights;
            }
            
            int count = mesh.blendShapeCount;
            if (proxy.sharedMesh.blendShapeCount > count)
            {
                for (int i = count; i < proxy.sharedMesh.blendShapeCount; i++)
                {
                    weights.Add(i, 0f);
                }
            }
            for (int i = 0; i < count; i++)
            {
                float weight = proxy.GetBlendShapeWeight(i);
                if (weight > 0)
                {
                    string blendShapeName = mesh.GetBlendShapeName(i);
                    if (skipShapeName != null && blendShapeName == skipShapeName) continue;
                    
                    var current = _component.m_TargetShapes.FirstOrDefault(t => t.m_TargetShapeName == blendShapeName);
                    if (current != null)
                    {
                        UpdateWeights(weights, proxy, current, weight);
                    }
                }
            }
            return weights;
        }

        #endregion

        #region Preview Mesh Management

        /// <summary>
        /// Computes which blend shapes need .inv shapes (active non-target shapes)
        /// </summary>
        private static HashSet<string> ComputeNeededInvShapes(FaceBlendShapeFixComponent component, SkinnedMeshRenderer source)
        {
            var result = new HashSet<string>();
            var mesh = source.sharedMesh;
            if (mesh == null) return result;

            // var targetNames = component?.m_TargetShapes?
            //     .Select(ts => ts.m_TargetShapeName)
            //     .ToHashSet() ?? new HashSet<string>();

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                //if (targetNames.Contains(name)) continue; // Skip target shapes

                if (source.GetBlendShapeWeight(i) > 0f)
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>
        /// Computes which blend shapes need .add shapes (from additive blend data)
        /// </summary>
        private static HashSet<string> ComputeNeededAddShapes(FaceBlendShapeFixComponent component)
        {
            var result = new HashSet<string>();
            if (component?.m_TargetShapes == null) return result;

            foreach (TargetShape targetShape in component.m_TargetShapes)
            {
                if (targetShape?.m_AdditiveBlendData == null) continue;

                foreach (BlendData data in targetShape.m_AdditiveBlendData)
                {
                    if (!string.IsNullOrEmpty(data?.m_TargetShapeName))
                    {
                        result.Add(data.m_TargetShapeName);
                    }
                }
            }
            return result;
        }

        private void RebuildPreviewMesh(HashSet<string> inverseShapes, HashSet<string> additiveShapes)
        {
            var mesh = _sourceRenderer.sharedMesh;
            if (mesh == null) return;

            if (_previewData.Mesh != null)
            {
                Object.DestroyImmediate(_previewData.Mesh);
            }

            _previewData.Mesh = PreviewMeshBuilder.BuildPreviewMesh(mesh, inverseShapes, additiveShapes, _smoothWidth);
            _previewData.InverseShapes = inverseShapes;
            _previewData.AdditiveShapes = additiveShapes;
        }

        #endregion
    }
}

#endif