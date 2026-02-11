using System;
using System.Collections.Generic;
using System.Linq;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Triturbo.FaceBlendShapeFix
{
    /// <summary>
    /// Provides utilities for building combined blend-shape delta buffers while honoring split left/right weights.
    /// </summary>
    internal static class MeshBlendShapeProcessor
    {
        public static Mesh BakeCorrectedShapes(FaceBlendShapeFixComponent component)
        {
            var smr = component.m_TargetRenderer != null
                ? component.m_TargetRenderer
                : component.GetComponent<SkinnedMeshRenderer>();
            Mesh oldMesh = smr.sharedMesh;
            Mesh newMesh = Object.Instantiate(oldMesh);
            newMesh.name = oldMesh.name;
            newMesh.ClearBlendShapes();
            var definitionLookup = BuildDefinitionLookup(component.m_BlendShapeDefinitions);
            
            for (int i = 0; i < oldMesh.blendShapeCount; i++)
            {
                string name = oldMesh.GetBlendShapeName(i);
                TargetShape targetShape = component.m_TargetShapes.FirstOrDefault(ts => ts.m_TargetShapeName == name);
                if (targetShape != null)
                {
                    Vector3[] inverseVertices;
                    Vector3[] inverseNormals;
                    Vector3[] inverseTangents;
                    Vector3[] additiveVertices;
                    Vector3[] additiveNormals;
                    Vector3[] additiveTangents;
                    (inverseVertices, inverseNormals, inverseTangents, additiveVertices, additiveNormals, additiveTangents) =
                        GetCombinedBlendShapes(smr, targetShape, definitionLookup, component.m_SmoothWidth);
                    
                    int frameCount = oldMesh.GetBlendShapeFrameCount(i);
                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = oldMesh.GetBlendShapeFrameWeight(i, f);
                        Vector3[] deltaVertices = new Vector3[oldMesh.vertexCount];
                        Vector3[] deltaNormals = new Vector3[oldMesh.vertexCount];
                        Vector3[] deltaTangents = new Vector3[oldMesh.vertexCount];
                        oldMesh.GetBlendShapeFrameVertices(i, f, deltaVertices, deltaNormals, deltaTangents);
                        for (int j = 0; j < oldMesh.vertexCount; j++)
                        {
                            deltaVertices[j] *= targetShape.m_Weight;
                            deltaNormals[j] *= targetShape.m_Weight;
                            deltaTangents[j] *= targetShape.m_Weight;
                            
                            deltaVertices[j] -= inverseVertices[j] * weight / 100f;
                            deltaNormals[j] -= inverseNormals[j] * weight / 100f;
                            deltaTangents[j] -= inverseTangents[j] * weight / 100f;

                            deltaVertices[j] += additiveVertices[j] * weight / 100f;
                            deltaNormals[j] += additiveNormals[j] * weight / 100f;
                            deltaTangents[j] += additiveTangents[j] * weight / 100f;
                        }
                        newMesh.AddBlendShapeFrame(name, weight, deltaVertices, deltaNormals, deltaTangents);
                    }
                }
                else
                {
                    int frameCount = oldMesh.GetBlendShapeFrameCount(i);
                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = oldMesh.GetBlendShapeFrameWeight(i, f);
                        Vector3[] deltaVertices = new Vector3[oldMesh.vertexCount];
                        Vector3[] deltaNormals = new Vector3[oldMesh.vertexCount];
                        Vector3[] deltaTangents = new Vector3[oldMesh.vertexCount];
                        oldMesh.GetBlendShapeFrameVertices(i, f, deltaVertices, deltaNormals, deltaTangents);
                        newMesh.AddBlendShapeFrame(name, weight, deltaVertices, deltaNormals, deltaTangents);
                    }
                }
                
            }
            return newMesh;
        }
        
        
        
        private static readonly Vector3[] s_Empty = Array.Empty<Vector3>();
        
        // Builds combined delta buffers for every referenced blend shape while honoring left/right splits.
        public static (Vector3[], Vector3[], Vector3[], Vector3[], Vector3[], Vector3[]) GetCombinedBlendShapes(
            SkinnedMeshRenderer smr,
            TargetShape targetShape,
            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup,
            float smoothWidth)
        {
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null)
            {
                return (s_Empty, s_Empty, s_Empty, s_Empty, s_Empty, s_Empty);
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount == 0)
            {
                return (s_Empty, s_Empty, s_Empty, s_Empty, s_Empty, s_Empty);
            }

            bool useGlobalDefinitions = targetShape != null && targetShape.m_UseGlobalDefinitions;
            ShapeType targetShapeType = targetShape != null ? targetShape.m_TargetShapeType : ShapeType.BothEyes;

            Vector3[] inverseDeltaVertices = new Vector3[vertexCount];
            Vector3[] inverseDeltaNormals = new Vector3[vertexCount];
            Vector3[] inverseDeltaTangents = new Vector3[vertexCount];
            
            Vector3[] additiveDeltaVertices = new Vector3[vertexCount];
            Vector3[] additiveDeltaNormals = new Vector3[vertexCount];
            Vector3[] additiveDeltaTangents = new Vector3[vertexCount];

            Vector3[] vertices = mesh.vertices;

            
            //Reset Blend
            var blendDataArray = targetShape?.GetBlendData(definitionLookup);
            if (blendDataArray != null && blendDataArray.Count > 0)
            {
                AccumulateBlendDataList(mesh, smr, blendDataArray, useGlobalDefinitions, targetShapeType,
                    definitionLookup, vertices, inverseDeltaVertices, inverseDeltaNormals, inverseDeltaTangents, smoothWidth);
            }


            //Additive Blend
            var additionalBlendData = targetShape?.m_AdditiveBlendData;
            if (additionalBlendData != null && additionalBlendData.Length > 0)
            {
                AccumulateAdditiveBlendDataList(mesh, smr, additionalBlendData,
                    vertices, additiveDeltaVertices, additiveDeltaNormals, additiveDeltaTangents, smoothWidth);
            }

            return (inverseDeltaVertices, inverseDeltaNormals, inverseDeltaTangents,
                additiveDeltaVertices, additiveDeltaNormals, additiveDeltaTangents);
        }

        private static void AccumulateBlendDataList(
            Mesh mesh,
            SkinnedMeshRenderer smr,
            IEnumerable<BlendData> blendDataArray,
            bool useGlobalDefinitions,
            ShapeType targetShapeType,
            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup,
            Vector3[] vertices,
            Vector3[] totalDeltaVertices,
            Vector3[] totalDeltaNormals,
            Vector3[] totalDeltaTangents,
            float smoothWidth)
        {
            if (mesh == null || smr == null || blendDataArray == null)
            {
                return;
            }

            foreach (var data in blendDataArray)
            {
                if (data == null)
                {
                    continue;
                }

                int shapeIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName);
                if (shapeIndex < 0)
                {
                    continue;
                }

                float smrWeight = smr.GetBlendShapeWeight(shapeIndex);
                if (Mathf.Approximately(smrWeight, 0f))
                {
                    continue;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                if (frameCount == 0)
                {
                    continue;
                }

                BlendShapeDefinition definition = null;
                bool useGlobalForEntry = useGlobalDefinitions &&
                                         definitionLookup != null &&
                                         !string.IsNullOrEmpty(data.m_TargetShapeName) &&
                                         definitionLookup.TryGetValue(data.m_TargetShapeName, out definition);

                if (frameCount > 1)
                {
                    // AccumulateMultiFrame(mesh, shapeIndex, smrWeight, data, vertices,
                    //     totalDeltaVertices, totalDeltaNormals, totalDeltaTangents,
                    //     useGlobalForEntry, targetShapeType, definition, definitionLookup);

                    AccumulateSingleFrame(mesh, shapeIndex, smrWeight, data, vertices,
                        totalDeltaVertices, totalDeltaNormals, totalDeltaTangents, smoothWidth, frameCount-1);
                }
                else
                {
                    AccumulateSingleFrame(mesh, shapeIndex, smrWeight, data, vertices,
                        totalDeltaVertices, totalDeltaNormals, totalDeltaTangents, smoothWidth);
                }
            }
        }
        
        
        private static void AccumulateAdditiveBlendDataList(
            Mesh mesh,
            SkinnedMeshRenderer smr,
            IEnumerable<BlendData> blendDataArray,
            Vector3[] vertices,
            Vector3[] totalDeltaVertices,
            Vector3[] totalDeltaNormals,
            Vector3[] totalDeltaTangents,
            float smoothWidth)
        {
            if (mesh == null || smr == null || blendDataArray == null)
            {
                return;
            }

            foreach (var data in blendDataArray)
            {
                if (data == null)
                {
                    continue;
                }

                int shapeIndex = mesh.GetBlendShapeIndex(data.m_TargetShapeName);
                if (shapeIndex < 0)
                {
                    continue;
                }
                
                int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
                if (frameCount == 0)
                {
                    continue;
                }
                AccumulateSingleFrame(mesh, shapeIndex, 100f, data, vertices,
                    totalDeltaVertices, totalDeltaNormals, totalDeltaTangents, smoothWidth, frameCount-1);
  
            }
        }

        // Samples single frame shapes and pushes their deltas into the accumulator using per-vertex weights.
        private static void AccumulateSingleFrame(
            Mesh mesh,
            int shapeIndex,
            float intensity, // [0, 100]
            BlendData data,
            Vector3[] vertices,
            Vector3[] totalDeltaVertices,
            Vector3[] totalDeltaNormals,
            Vector3[] totalDeltaTangents,
            float smoothWidth,
            int targetFrame = 0)
        {
            int vertexCount = mesh.vertexCount;

            Vector3[] deltaVertices = new Vector3[vertexCount];
            Vector3[] deltaNormals = new Vector3[vertexCount];
            Vector3[] deltaTangents = new Vector3[vertexCount];

            mesh.GetBlendShapeFrameVertices(shapeIndex, targetFrame, deltaVertices, deltaNormals, deltaTangents);

            for (int i = 0; i < vertexCount; i++)
            {
                float vertexWeight = ResolveVertexWeight(intensity, data, vertices[i].x, smoothWidth);
                if (Mathf.Approximately(vertexWeight, 0f))
                {
                    continue;
                }
                float multiplier = vertexWeight / 100f;
                totalDeltaVertices[i] += deltaVertices[i] * multiplier;
                totalDeltaNormals[i] += deltaNormals[i] * multiplier;
                totalDeltaTangents[i] += deltaTangents[i] * multiplier;
            }
        }

        // Interpolates multi-frame shapes at the active weight and applies split scaling per vertex.
        private static void AccumulateMultiFrame(
            Mesh mesh,
            int shapeIndex,
            float smrWeight,
            BlendData data,
            Vector3[] vertices,
            Vector3[] totalDeltaVertices,
            Vector3[] totalDeltaNormals,
            Vector3[] totalDeltaTangents,
            bool useGlobalDefinitions,
            ShapeType targetShapeType,
            BlendShapeDefinition definition,
            IReadOnlyDictionary<string, BlendShapeDefinition> definitionLookup,
            float smoothWidth)
        {
            float normalizedAverage = useGlobalDefinitions && definition != null
                ? ResolveGlobalAverageWeight(definition, targetShapeType)
                : data.m_Weight;
            
            float averagedWeight = smrWeight * normalizedAverage;
            if (Mathf.Approximately(averagedWeight, 0f))
            {
                return;
            }
        
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            int lowerFrame = -1;
            int upperFrame = -1;
        
            for (int f = 0; f < frameCount; f++)
            {
                float frameWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, f);
                if (frameWeight <= averagedWeight)
                {
                    lowerFrame = f;
                }
                if (frameWeight >= averagedWeight)
                {
                    upperFrame = f;
                    break;
                }
            }
        
            if (lowerFrame < 0) lowerFrame = Math.Max(upperFrame, 0);
            if (upperFrame < 0) upperFrame = lowerFrame;
        
            Vector3[] lowerVerts = new Vector3[mesh.vertexCount];
            Vector3[] lowerNormals = new Vector3[mesh.vertexCount];
            Vector3[] lowerTangents = new Vector3[mesh.vertexCount];
        
            Vector3[] upperVerts = new Vector3[mesh.vertexCount];
            Vector3[] upperNormals = new Vector3[mesh.vertexCount];
            Vector3[] upperTangents = new Vector3[mesh.vertexCount];
        
            mesh.GetBlendShapeFrameVertices(shapeIndex, lowerFrame, lowerVerts, lowerNormals, lowerTangents);
            mesh.GetBlendShapeFrameVertices(shapeIndex, upperFrame, upperVerts, upperNormals, upperTangents);
        
            float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lowerFrame);
            float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, upperFrame);
            float denominator = upperWeight - lowerWeight;
            float t = Mathf.Approximately(denominator, 0f)
                ? 0f
                : Mathf.Clamp01((averagedWeight - lowerWeight) / denominator);
        
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                float vertexWeight = ResolveVertexWeight(
                    smrWeight,
                    data,
                    vertices[i].x,
                    smoothWidth);
                if (Mathf.Approximately(vertexWeight, 0f))
                {
                    continue;
                }
        
                float weightRatio = SafeDivide(vertexWeight, averagedWeight);
                if (Mathf.Approximately(weightRatio, 0f))
                {
                    continue;
                }
        
                Vector3 blendedVertex = Vector3.Lerp(lowerVerts[i], upperVerts[i], t);
                Vector3 blendedNormal = Vector3.Lerp(lowerNormals[i], upperNormals[i], t);
                Vector3 blendedTangent = Vector3.Lerp(lowerTangents[i], upperTangents[i], t);
        
                totalDeltaVertices[i] += blendedVertex * weightRatio;
                totalDeltaNormals[i] += blendedNormal * weightRatio;
                totalDeltaTangents[i] += blendedTangent * weightRatio;
            }
        }

        // Calculates the effective weight for a vertex based on its X position and smooth width.
        private static float ResolveVertexWeight(
            float smrWeight,
            BlendData data,
            float vertexX,
            float smoothWidth)
        {
            if (data == null)
            {
                return 0f;
            }

            float normalizedWeight;
            if (data.m_SplitLeftRight)
            {
                var (leftFactor, rightFactor) = CalculateSideFactor(vertexX, smoothWidth);
                normalizedWeight = data.m_LeftWeight * leftFactor + data.m_RightWeight * rightFactor;
            }
            else
            {
                normalizedWeight = data.m_Weight;
            }

            return smrWeight * Mathf.Clamp01(normalizedWeight);
        }

        private static float ResolveGlobalDefinitionWeight(
            BlendShapeDefinition definition,
            ShapeType targetShapeType,
            bool isLeft)
        {
            if (definition == null || definition.m_Protected)
            {
                return 0f;
            }

            switch (targetShapeType)
            {
                case ShapeType.Mouth:
                    return definition.m_MouthWeight;
                case ShapeType.BothEyes:
                    return isLeft ? definition.m_LeftEyeWeight : definition.m_RightEyeWeight;
                case ShapeType.LeftEye:
                    return isLeft ? definition.m_LeftEyeWeight : 0f;
                case ShapeType.RightEye:
                    return isLeft ? 0f : definition.m_RightEyeWeight;
                default:
                    return 0f;
            }
        }

        private static float ResolveGlobalAverageWeight(BlendShapeDefinition definition, ShapeType targetShapeType)
        {
            if (definition == null || definition.m_Protected)
            {
                return 0f;
            }

            switch (targetShapeType)
            {
                case ShapeType.Mouth:
                    return definition.m_MouthWeight;
                case ShapeType.BothEyes:
                    return (definition.m_LeftEyeWeight + definition.m_RightEyeWeight) * 0.5f;
                case ShapeType.LeftEye:
                    return definition.m_LeftEyeWeight;
                case ShapeType.RightEye:
                    return definition.m_RightEyeWeight;
                default:
                    return 0f;
            }
        }

        private static IReadOnlyDictionary<string, BlendShapeDefinition> BuildDefinitionLookup(
            BlendShapeDefinition[] definitions)
        {
            if (definitions == null || definitions.Length == 0)
            {
                return null;
            }

            var lookup = new Dictionary<string, BlendShapeDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.m_BlendShapeName))
                {
                    continue;
                }

                lookup[definition.m_BlendShapeName] = definition;
            }

            return lookup;
        }

        // Avoids NaN/Inf by guarding division when the denominator is ~0.
        private static float SafeDivide(float numerator, float denominator)
        {
            return Mathf.Approximately(denominator, 0f) ? 0f : numerator / denominator;
        }

        /// <summary>
        /// Calculates left/right blend factors for a vertex based on its X position and smooth width.
        /// </summary>
        /// <param name="x">Vertex X position in local space</param>
        /// <param name="smoothWidth">Width of the transition zone. 0 = hard cut at x=0</param>
        /// <returns>Tuple of (leftFactor, rightFactor) where each is in range [0,1]</returns>
        internal static (float leftFactor, float rightFactor) CalculateSideFactor(float x, float smoothWidth)
        {
            if (smoothWidth <= 0f)
            {
                return x < 0f ? (1f, 0f) : (0f, 1f);
            }

            if (x <= -smoothWidth) return (1f, 0f);
            if (x >= smoothWidth) return (0f, 1f);

            float t = (x + smoothWidth) / (2f * smoothWidth);
            return (1f - t, t);
        }
    }
}
