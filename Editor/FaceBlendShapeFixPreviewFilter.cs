#if FBF_NDMF
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using Triturbo.FaceBlendShapeFix.Runtime;
using Unity.Profiling;
using UnityEngine;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;


namespace Triturbo.FaceBlendShapeFix
{
    internal sealed class FaceBlendShapeFixPreviewFilter : IRenderFilter
    {
        #region Fields

        private static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "Face BlendShape Fix Preview",
            "triturbo.face-blendshape-fix/preview",
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
        private static readonly StringComparer NameComparer = StringComparer.Ordinal;
        private static readonly ProfilerMarker RefreshMarker = new("FaceBlendShapeFixPreview.Refresh");
        private static readonly ProfilerMarker OnFrameMarker = new("FaceBlendShapeFixPreview.OnFrame");
        private static readonly ProfilerMarker RebuildPreviewMeshMarker = new("FaceBlendShapeFixPreview.RebuildPreviewMesh");
        private static readonly ProfilerMarker RebuildTargetCachesMarker = new("FaceBlendShapeFixPreview.RebuildTargetCaches");
        private static readonly ProfilerMarker ApplyCachedTargetsMarker = new("FaceBlendShapeFixPreview.ApplyCachedTargets");
        private static readonly ProfilerMarker ApplyPreviewTargetMarker = new("FaceBlendShapeFixPreview.ApplyPreviewTarget");

        private struct PreviewMeshData
        {
            public Mesh Mesh;
            public HashSet<string> InverseShapes;
            public HashSet<string> AdditiveShapes;
        }

        // Resolved inverse preview data for one BlendData entry. All string-based lookups are
        // paid during Refresh so OnFrame only touches numeric indices and multipliers.
        private struct InverseEntryCache
        {
            public int BaseIndex;
            public int LeftIndex;
            public int RightIndex;
            public float LeftWeight;
            public float RightWeight;
        }

        // Resolved additive preview data for one additive entry. The split/non-split weighting
        // decision is also flattened here so the frame loop does not branch on authoring data.
        private struct AdditiveEntryCache
        {
            public int LeftIndex;
            public int RightIndex;
            public float LeftWeight;
            public float RightWeight;
        }

        // Per-target cache consumed by OnFrame. This mirrors the authoring target shape, but all
        // expensive mesh name resolution has already been converted into direct blend-shape indices.
        private sealed class TargetCache
        {
            public int ComponentTargetIndex;
            public string TargetName;
            public int TargetBlendShapeIndex;
            public float TargetWeight;
            public InverseEntryCache[] InverseEntries = Array.Empty<InverseEntryCache>();
            public AdditiveEntryCache[] AdditiveEntries = Array.Empty<AdditiveEntryCache>();
        }


        private readonly FaceBlendShapeFixComponent _component;
        private readonly SkinnedMeshRenderer _sourceRenderer;
        // Reused scratch state keeps the steady-state frame path allocation-free.
        private readonly Dictionary<int, float> _scratchWeights = new();
        private readonly Dictionary<string, TargetCache> _targetCacheByName = new(NameComparer);
        private readonly Dictionary<int, TargetCache> _targetCacheByComponentIndex = new();
        private readonly List<TargetCache> _targetCaches = new();
        // Generated blend shapes live in the appended range of the preview mesh. We cache those
        // indices once so each frame can zero only synthetic preview shapes, not the whole mesh.
        private readonly List<int> _generatedBlendShapeIndices = new();
        private PreviewMeshData _previewData;
        private float _smoothWidth;
        private int _configurationSignature;


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
            if (_component == null || _sourceRenderer == null)
            {
                return Task.FromResult<IRenderFilterNode>(this);
            }

            bool logEnabled = FaceBlendShapeFixPreviewProfiling.Enabled;
            long startTicks = logEnabled ? Stopwatch.GetTimestamp() : 0L;
            bool meshRebuilt = false;
            bool cacheRebuilt = false;

            using (RefreshMarker.Auto())
            {
                // Refresh is the only place where we intentionally do the expensive work:
                // 1. observe the current preview topology requirements
                // 2. rebuild the preview mesh if the required split-shape sets changed
                // 3. rebuild cached target metadata if either the mesh changed or authoring data changed
                //
                // This keeps steady-state OnFrame limited to cached numeric indices and one reusable
                // scratch dictionary, which is the main optimization in this pass.
                HashSet<string> neededInvShapes = context.Observe(_sourceRenderer, ComputeNeededInvShapes);
                HashSet<string> neededAddShapes = context.Observe(_component, ComputeNeededAddShapes);
                int configurationSignature = context.Observe(_component, ComputeConfigurationSignature);
                float currentSmoothWidth = context.Observe(_component, c => c.m_SmoothWidth);

                bool needsMeshRebuild =
                    _previewData.Mesh == null ||
                    (updatedAspects & RenderAspects.Mesh) != 0 ||
                    !ShapeSetsEqual(neededInvShapes, _previewData.InverseShapes) ||
                    !ShapeSetsEqual(neededAddShapes, _previewData.AdditiveShapes) ||
                    Math.Abs(currentSmoothWidth - _smoothWidth) > float.Epsilon;

                _smoothWidth = currentSmoothWidth;

                if (needsMeshRebuild)
                {
                    meshRebuilt = true;
                    using (RebuildPreviewMeshMarker.Auto())
                    {
                        RebuildPreviewMesh(neededInvShapes, neededAddShapes);
                    }
                }

                bool needsCacheRebuild =
                    needsMeshRebuild ||
                    _configurationSignature != configurationSignature;

                if (needsCacheRebuild)
                {
                    cacheRebuilt = true;
                    _configurationSignature = configurationSignature;
                    using (RebuildTargetCachesMarker.Auto())
                    {
                        RebuildTargetCaches();
                    }
                }
            }

            if (logEnabled)
            {
                FaceBlendShapeFixPreviewProfiling.RecordRefresh(
                    Stopwatch.GetTimestamp() - startTicks,
                    meshRebuilt,
                    cacheRebuilt);
            }

            return Task.FromResult<IRenderFilterNode>(this);
        }

        public void OnFrame(Renderer original, Renderer proxy)
        {
            if (original is not SkinnedMeshRenderer || proxy is not SkinnedMeshRenderer proxySmr)
            {
                return;
            }

            bool logEnabled = FaceBlendShapeFixPreviewProfiling.Enabled;
            long startTicks = logEnabled ? Stopwatch.GetTimestamp() : 0L;
            int appliedTargetCount = 0;
            PreviewRequest previewRequest = default;
            bool hasPreviewRequest = _component != null && _component.TryGetPreviewRequest(out previewRequest);
            bool passivePreviewEnabled = FaceBlendShapeFixPreviewSettings.PassivePreviewEnabled;

            using (OnFrameMarker.Auto())
            {
                // Explicit preview always wins. Passive preview is the legacy "read live target weights
                // every frame" behavior and can now be globally disabled. When both are off we leave the
                // proxy visually inert by not assigning the preview mesh and by skipping all cached work.
                string skipShapeName = null;
                TargetCache previewTargetCache = null;

                if (hasPreviewRequest &&
                    TryGetPreviewTargetCache(previewRequest, out previewTargetCache))
                {
                    skipShapeName = previewTargetCache.TargetName;
                }
                else if (hasPreviewRequest && previewRequest.Target != null)
                {
                    skipShapeName = previewRequest.Target.m_TargetShapeName;
                }

                if (hasPreviewRequest || passivePreviewEnabled)
                {
                    if (_previewData.Mesh != null)
                    {
                        proxySmr.sharedMesh = _previewData.Mesh;
                    }

                    // Clear only the reusable scratch state. All generated preview-only shapes are then
                    // seeded to zero so any omitted writes this frame do not leave stale .inv/.add values
                    // behind from a previous preview target or previous live weights.
                    _scratchWeights.Clear();
                    ZeroGeneratedBlendShapeWeights(_scratchWeights);

                    using (ApplyCachedTargetsMarker.Auto())
                    {
                        foreach (TargetCache targetCache in _targetCaches)
                        {
                            if (skipShapeName != null && targetCache.TargetName == skipShapeName)
                            {
                                continue;
                            }

                            float baseWeight = proxySmr.GetBlendShapeWeight(targetCache.TargetBlendShapeIndex);
                            if (baseWeight <= 0f)
                            {
                                continue;
                            }

                            ApplyTargetWeights(_scratchWeights, proxySmr, targetCache, baseWeight);
                            appliedTargetCount++;
                        }
                    }

                    if (hasPreviewRequest && previewTargetCache != null)
                    {
                        using (ApplyPreviewTargetMarker.Auto())
                        {
                            ApplyTargetWeights(_scratchWeights, proxySmr, previewTargetCache, previewRequest.Weight);
                        }

                        appliedTargetCount++;
                    }

                    foreach (KeyValuePair<int, float> weight in _scratchWeights)
                    {
                        proxySmr.SetBlendShapeWeight(weight.Key, weight.Value);
                    }
                }
            }

            if (logEnabled)
            {
                FaceBlendShapeFixPreviewProfiling.RecordFrame(
                    Stopwatch.GetTimestamp() - startTicks,
                    appliedTargetCount,
                    _targetCaches.Count,
                    _generatedBlendShapeIndices.Count);
            }
        }

        #endregion

        #region Weight Calculation

        private static void ApplyTargetWeights(
            Dictionary<int, float> weights,
            SkinnedMeshRenderer proxy,
            TargetCache targetCache,
            float weight)
        {
            // The target cache already contains fully resolved indices, so the frame path only reads
            // current base weights from the proxy and accumulates numeric output weights.
            weights[targetCache.TargetBlendShapeIndex] = weight * targetCache.TargetWeight;

            float factor = weight / 100f;
            foreach (InverseEntryCache entry in targetCache.InverseEntries)
            {
                float baseWeight = proxy.GetBlendShapeWeight(entry.BaseIndex);
                if (baseWeight <= 0f)
                {
                    continue;
                }

                AccumulateWeight(weights, entry.RightIndex, baseWeight * entry.RightWeight * factor);
                AccumulateWeight(weights, entry.LeftIndex, baseWeight * entry.LeftWeight * factor);
            }

            foreach (AdditiveEntryCache entry in targetCache.AdditiveEntries)
            {
                AccumulateWeight(weights, entry.RightIndex, entry.RightWeight * weight);
                AccumulateWeight(weights, entry.LeftIndex, entry.LeftWeight * weight);
            }
        }

        private static void AccumulateWeight(Dictionary<int, float> weights, int index, float delta)
        {
            if (index < 0 || Mathf.Approximately(delta, 0f))
            {
                return;
            }

            if (!weights.TryAdd(index, delta))
            {
                weights[index] += delta;
            }
        }

        private void ZeroGeneratedBlendShapeWeights(Dictionary<int, float> weights)
        {
            // Only synthetic preview shapes need to be force-zeroed here. Base mesh blend shapes are
            // still driven by the proxy's existing weights and should not be overwritten pre-emptively.
            foreach (int index in _generatedBlendShapeIndices)
            {
                weights[index] = 0f;
            }
        }

        private bool TryGetPreviewTargetCache(PreviewRequest request, out TargetCache targetCache)
        {
            if (_targetCacheByComponentIndex.TryGetValue(request.TargetIndex, out targetCache))
            {
                return true;
            }

            if (request.Target != null && !string.IsNullOrEmpty(request.Target.m_TargetShapeName))
            {
                return _targetCacheByName.TryGetValue(request.Target.m_TargetShapeName, out targetCache);
            }

            targetCache = null;
            return false;
        }

        #endregion

        #region Cache Management

        private void RebuildTargetCaches()
        {
            // Cache rebuilds are coarse on purpose. Refresh already knows when either the preview mesh
            // topology or the authoring configuration changed, so rebuilding everything in one pass keeps
            // the implementation simple and guarantees OnFrame never has to validate names or definitions.
            ClearTargetCaches();

            Mesh sourceMesh = _sourceRenderer.sharedMesh;
            Mesh previewMesh = _previewData.Mesh;
            if (sourceMesh == null || previewMesh == null)
            {
                return;
            }

            CacheGeneratedBlendShapeIndices(sourceMesh.blendShapeCount, previewMesh.blendShapeCount);

            TargetShape[] targetShapes = _component.m_TargetShapes;
            if (targetShapes == null || targetShapes.Length == 0)
            {
                return;
            }

            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup =
                BuildDefinitionLookup(_component.m_BlendShapeDefinitions);

            for (int i = 0; i < targetShapes.Length; i++)
            {
                TargetCache targetCache = BuildTargetCache(previewMesh, targetShapes[i], i, definitionLookup);
                if (targetCache == null)
                {
                    continue;
                }

                _targetCacheByComponentIndex[i] = targetCache;
                if (_targetCacheByName.ContainsKey(targetCache.TargetName))
                {
                    continue;
                }

                _targetCacheByName.Add(targetCache.TargetName, targetCache);
                _targetCaches.Add(targetCache);
            }
        }

        private void ClearTargetCaches()
        {
            _scratchWeights.Clear();
            _targetCacheByName.Clear();
            _targetCacheByComponentIndex.Clear();
            _targetCaches.Clear();
            _generatedBlendShapeIndices.Clear();
        }

        private void CacheGeneratedBlendShapeIndices(int sourceBlendShapeCount, int previewBlendShapeCount)
        {
            // PreviewMeshBuilder appends generated .inv/.add shapes after the original mesh range.
            // Caching that appended range once avoids scanning the mesh or rebuilding a dictionary
            // just to clear preview-only weights every frame.
            for (int i = sourceBlendShapeCount; i < previewBlendShapeCount; i++)
            {
                _generatedBlendShapeIndices.Add(i);
            }
        }

        private static TargetCache BuildTargetCache(
            Mesh previewMesh,
            TargetShape targetShape,
            int componentTargetIndex,
            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup)
        {
            if (previewMesh == null ||
                targetShape == null ||
                string.IsNullOrEmpty(targetShape.m_TargetShapeName))
            {
                return null;
            }

            int targetBlendShapeIndex = previewMesh.GetBlendShapeIndex(targetShape.m_TargetShapeName);
            if (targetBlendShapeIndex < 0)
            {
                return null;
            }

            // Invalid targets are filtered out once during cache build. After that, OnFrame can
            // trust every cached target and stay free of string lookups and structural checks.
            return new TargetCache
            {
                ComponentTargetIndex = componentTargetIndex,
                TargetName = targetShape.m_TargetShapeName,
                TargetBlendShapeIndex = targetBlendShapeIndex,
                TargetWeight = targetShape.m_Weight,
                InverseEntries = BuildInverseEntryCaches(previewMesh, targetShape, definitionLookup),
                AdditiveEntries = BuildAdditiveEntryCaches(previewMesh, targetShape.m_AdditiveBlendData)
            };
        }

        private static InverseEntryCache[] BuildInverseEntryCaches(
            Mesh previewMesh,
            TargetShape targetShape,
            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup)
        {
            if (previewMesh == null || targetShape?.m_BlendData == null || targetShape.m_BlendData.Length == 0)
            {
                return Array.Empty<InverseEntryCache>();
            }

            // Resolve global definitions once during cache build. Calling GetBlendData in OnFrame would
            // re-expand authoring data every frame, which is exactly what this optimization avoids.
            IReadOnlyList<BlendData> resolvedBlendData = targetShape.GetBlendData(definitionLookup);
            if (resolvedBlendData == null || resolvedBlendData.Count == 0)
            {
                return Array.Empty<InverseEntryCache>();
            }

            var entries = new List<InverseEntryCache>(resolvedBlendData.Count);
            foreach (BlendData data in resolvedBlendData)
            {
                if (data == null || string.IsNullOrEmpty(data.m_TargetShapeName))
                {
                    continue;
                }

                // All native GetBlendShapeIndex(string) calls and suffix-name construction are moved
                // here so the steady-state frame loop never pays repeated O(n) name resolution costs.
                int baseIndex = previewMesh.GetBlendShapeIndex(data.m_TargetShapeName);
                if (baseIndex < 0)
                {
                    continue;
                }

                string rightName = data.m_TargetShapeName + ".inv.R";
                string leftName = data.m_TargetShapeName + ".inv.L";
                int rightIndex = previewMesh.GetBlendShapeIndex(rightName);
                int leftIndex = previewMesh.GetBlendShapeIndex(leftName);
                if (rightIndex < 0 || leftIndex < 0)
                {
                    continue;
                }

                entries.Add(new InverseEntryCache
                {
                    BaseIndex = baseIndex,
                    LeftIndex = leftIndex,
                    RightIndex = rightIndex,
                    LeftWeight = data.m_LeftWeight,
                    RightWeight = data.m_RightWeight
                });
            }

            return entries.Count == 0 ? Array.Empty<InverseEntryCache>() : entries.ToArray();
        }

        private static AdditiveEntryCache[] BuildAdditiveEntryCaches(Mesh previewMesh, BlendData[] additiveBlendData)
        {
            if (previewMesh == null || additiveBlendData == null || additiveBlendData.Length == 0)
            {
                return Array.Empty<AdditiveEntryCache>();
            }

            var entries = new List<AdditiveEntryCache>(additiveBlendData.Length);
            foreach (BlendData data in additiveBlendData)
            {
                if (data == null || string.IsNullOrEmpty(data.m_TargetShapeName))
                {
                    continue;
                }

                // Additive suffix resolution is also flattened up front. OnFrame receives only the
                // left/right numeric targets and the final multipliers to accumulate.
                string rightName = data.m_TargetShapeName + ".add.R";
                string leftName = data.m_TargetShapeName + ".add.L";
                int rightIndex = previewMesh.GetBlendShapeIndex(rightName);
                int leftIndex = previewMesh.GetBlendShapeIndex(leftName);
                if (rightIndex < 0 || leftIndex < 0)
                {
                    continue;
                }

                entries.Add(new AdditiveEntryCache
                {
                    LeftIndex = leftIndex,
                    RightIndex = rightIndex,
                    LeftWeight = data.m_SplitLeftRight ? data.m_LeftWeight : data.m_Weight,
                    RightWeight = data.m_SplitLeftRight ? data.m_RightWeight : data.m_Weight
                });
            }

            return entries.Count == 0 ? Array.Empty<AdditiveEntryCache>() : entries.ToArray();
        }

        private static IReadOnlyDictionary<string, BlendShapeDefinition> BuildDefinitionLookup(
            BlendShapeDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0)
            {
                return null;
            }

            // One refresh-scoped lookup replaces repeated linear scans through global definitions when
            // target caches resolve their effective BlendData payload.
            var lookup = new Dictionary<string, BlendShapeDefinition>(definitions.Length, NameComparer);
            foreach (BlendShapeDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.m_BlendShapeName))
                {
                    continue;
                }

                lookup[definition.m_BlendShapeName] = definition;
            }

            return lookup;
        }

        #endregion

        #region Preview Mesh Management

        /// <summary>
        /// Computes which blend shapes need .inv shapes (active non-target shapes)
        /// </summary>
        private static HashSet<string> ComputeNeededInvShapes(SkinnedMeshRenderer source)
        {
            var result = new HashSet<string>(NameComparer);
            Mesh mesh = source != null ? source.sharedMesh : null;
            if (mesh == null)
            {
                return result;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
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
            var result = new HashSet<string>(NameComparer);
            if (component?.m_TargetShapes == null)
            {
                return result;
            }

            foreach (TargetShape targetShape in component.m_TargetShapes)
            {
                if (targetShape?.m_AdditiveBlendData == null)
                {
                    continue;
                }

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

        private static bool ShapeSetsEqual(HashSet<string> left, HashSet<string> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.SetEquals(right);
        }

        private static HashSet<string> CloneShapeSet(HashSet<string> source)
        {
            return source == null
                ? new HashSet<string>(NameComparer)
                : new HashSet<string>(source, NameComparer);
        }

        private static int ComputeConfigurationSignature(FaceBlendShapeFixComponent component)
        {
            // The signature intentionally covers authoring data that changes cached output even when
            // the preview mesh topology stays the same. That lets Refresh skip cache rebuilds in the
            // common steady-state case while still rebuilding immediately after edits or renames.
            unchecked
            {
                int hash = 17;
                AddHash(ref hash, component?.m_TargetShapes?.Length ?? 0);

                if (component?.m_TargetShapes != null)
                {
                    foreach (TargetShape targetShape in component.m_TargetShapes)
                    {
                        if (targetShape == null)
                        {
                            AddHash(ref hash, -1);
                            continue;
                        }

                        AddHash(ref hash, targetShape.m_TargetShapeName);
                        AddHash(ref hash, targetShape.m_Weight);
                        AddHash(ref hash, targetShape.m_UseGlobalDefinitions);
                        AddHash(ref hash, (int)targetShape.m_TargetShapeType);
                        AddHash(ref hash, targetShape.m_BlendData);
                        AddHash(ref hash, targetShape.m_AdditiveBlendData);
                    }
                }

                AddHash(ref hash, component?.m_BlendShapeDefinitions?.Length ?? 0);
                if (component?.m_BlendShapeDefinitions != null)
                {
                    foreach (BlendShapeDefinition definition in component.m_BlendShapeDefinitions)
                    {
                        if (definition == null)
                        {
                            AddHash(ref hash, -1);
                            continue;
                        }

                        AddHash(ref hash, definition.m_BlendShapeName);
                        AddHash(ref hash, definition.m_LeftEyeWeight);
                        AddHash(ref hash, definition.m_RightEyeWeight);
                        AddHash(ref hash, definition.m_MouthWeight);
                        AddHash(ref hash, definition.m_Protected);
                    }
                }

                return hash;
            }
        }

        private static void AddHash(ref int hash, BlendData[] blendDataArray)
        {
            AddHash(ref hash, blendDataArray?.Length ?? 0);
            if (blendDataArray == null)
            {
                return;
            }

            foreach (BlendData data in blendDataArray)
            {
                if (data == null)
                {
                    AddHash(ref hash, -1);
                    continue;
                }

                AddHash(ref hash, data.m_TargetShapeName);
                AddHash(ref hash, data.m_Weight);
                AddHash(ref hash, data.m_LeftWeight);
                AddHash(ref hash, data.m_RightWeight);
                AddHash(ref hash, data.m_SplitLeftRight);
            }
        }

        private static void AddHash(ref int hash, string value)
        {
            hash = (hash * 31) + (value != null ? NameComparer.GetHashCode(value) : 0);
        }

        private static void AddHash(ref int hash, float value)
        {
            hash = (hash * 31) + value.GetHashCode();
        }

        private static void AddHash(ref int hash, int value)
        {
            hash = (hash * 31) + value;
        }

        private static void AddHash(ref int hash, bool value)
        {
            hash = (hash * 31) + (value ? 1 : 0);
        }

        private void RebuildPreviewMesh(HashSet<string> inverseShapes, HashSet<string> additiveShapes)
        {
            Mesh mesh = _sourceRenderer.sharedMesh;
            if (_previewData.Mesh != null)
            {
                Object.DestroyImmediate(_previewData.Mesh);
                _previewData.Mesh = null;
            }

            _previewData.InverseShapes = CloneShapeSet(inverseShapes);
            _previewData.AdditiveShapes = CloneShapeSet(additiveShapes);

            if (mesh == null)
            {
                return;
            }

            // The preview mesh is rebuilt from the full required sets rather than only ever growing.
            // Using full set equality in Refresh means split shapes can both appear and disappear,
            // preventing long editor sessions from accumulating stale generated blend shapes.
            _previewData.Mesh = PreviewMeshBuilder.BuildPreviewMesh(mesh, inverseShapes, additiveShapes, _smoothWidth);
        }

        #endregion
    }
}

#endif
