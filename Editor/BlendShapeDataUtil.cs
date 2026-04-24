using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Triturbo.FaceBlendShapeFix.Runtime;
using UnityEditor;
using UnityEngine;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class BlendShapeDataUtil
    {
        internal enum BlendShapeComparisonMode
        {
            Average,
            Max
        }

        internal sealed class BlendShapeAnalysisCache
        {
            private readonly Dictionary<int, Vector3[]> _deltaVerticesByShapeIndex = new();

            public Mesh Mesh { get; }
            public Vector3[] Vertices { get; }
            public int VertexCount => Vertices.Length;

            public BlendShapeAnalysisCache(Mesh mesh)
            {
                Mesh = mesh;
                Vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
            }

            public bool TryGetDeltaVertices(int shapeIndex, out Vector3[] deltaVertices)
            {
                if (Mesh == null ||
                    shapeIndex < 0 ||
                    shapeIndex >= Mesh.blendShapeCount ||
                    Mesh.GetBlendShapeFrameCount(shapeIndex) <= 0)
                {
                    deltaVertices = null;
                    return false;
                }

                if (_deltaVerticesByShapeIndex.TryGetValue(shapeIndex, out deltaVertices))
                {
                    return true;
                }

                deltaVertices = new Vector3[Mesh.vertexCount];
                Mesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaVertices, null, null);
                _deltaVerticesByShapeIndex.Add(shapeIndex, deltaVertices);
                return true;
            }
        }

        private static BlendShapeAnalysisCache ResolveAnalysisCache(Mesh mesh, BlendShapeAnalysisCache cache)
        {
            if (mesh == null)
            {
                return null;
            }

            return cache != null && cache.Mesh == mesh
                ? cache
                : new BlendShapeAnalysisCache(mesh);
        }
        
        public static (float leftScore, float rightScore) CompareBlendShapesLeftRight(Mesh mesh, int indexA, int indexB, float weightA = 1f)
        {
            return CompareBlendShapesLeftRight(ResolveAnalysisCache(mesh, null), indexA, indexB, weightA);
        }

        internal static (float leftScore, float rightScore) CompareBlendShapesLeftRight(
            BlendShapeAnalysisCache cache,
            int indexA,
            int indexB,
            float weightA = 1f)
        {
            if (indexA == indexB)
            {
                return (1f, 1f);
            }

            if (cache == null ||
                cache.VertexCount == 0 ||
                !cache.TryGetDeltaVertices(indexA, out Vector3[] deltaA) ||
                !cache.TryGetDeltaVertices(indexB, out Vector3[] deltaB))
            {
                return (0f, 0f);
            }

            double dotLeft = 0;
            double magBLeft = 0;
            double dotRight = 0;
            double magBRight = 0;
            Vector3[] vertices = cache.Vertices;
            int count = cache.VertexCount;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                    continue;
                
                var current = Vector3.Dot(a, b);

                if (vertices[i].x < 0f)
                {
                    dotLeft += current > 0 ? current: -0.5 * current;
                    magBLeft += b.sqrMagnitude;
                }
                else
                {
                    dotRight += current > 0 ? current : -0.5 * current;
                    magBRight += b.sqrMagnitude;
                }
            }

            float leftScore = (float)((magBLeft < 1e-10) ? 0 : Math.Max(0, Math.Min(1, dotLeft / magBLeft)));
            float rightScore = (float)((magBRight < 1e-10) ? 0 : Math.Max(0, Math.Min(1, dotRight / magBRight)));

            float factor = 2 * weightA - 1;
 
            return (leftScore*factor, rightScore*factor);
        }

        public static (float leftScore, float rightScore) CompareBlendShapesLeftRight(
            Mesh mesh,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            return CompareBlendShapesLeftRight(ResolveAnalysisCache(mesh, null), referenceIndices, targetIndex, mode);
        }

        internal static (float leftScore, float rightScore) CompareBlendShapesLeftRight(
            BlendShapeAnalysisCache cache,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            if (cache == null || referenceIndices == null || referenceIndices.Count == 0)
            {
                return (0f, 0f);
            }

            float leftSum = 0f;
            float rightSum = 0f;
            float leftMax = 0f;
            float rightMax = 0f;
            int count = 0;

            foreach (int referenceIndex in referenceIndices)
            {
                if (referenceIndex < 0)
                {
                    continue;
                }

                (float leftScore, float rightScore) = CompareBlendShapesLeftRight(cache, referenceIndex, targetIndex);
                leftSum += leftScore;
                rightSum += rightScore;
                leftMax = Mathf.Max(leftMax, leftScore);
                rightMax = Mathf.Max(rightMax, rightScore);
                count++;
            }

            if (count == 0)
            {
                return (0f, 0f);
            }

            return mode == BlendShapeComparisonMode.Max
                ? (leftMax, rightMax)
                : (leftSum / count, rightSum / count);
        }
        
        public static float CompareBlendShapes(Mesh mesh, int indexA, int indexB)
        {
            return CompareBlendShapes(ResolveAnalysisCache(mesh, null), indexA, indexB);
        }

        internal static float CompareBlendShapes(BlendShapeAnalysisCache cache, int indexA, int indexB)
        {
            if (indexA == indexB)
            {
                return 1f;
            }

            if (cache == null ||
                cache.VertexCount == 0 ||
                !cache.TryGetDeltaVertices(indexA, out Vector3[] deltaA) ||
                !cache.TryGetDeltaVertices(indexB, out Vector3[] deltaB))
            {
                return 0f;
            }

            double dotSum = 0;
            double magB = 0;
            int count = cache.VertexCount;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                    continue;

                dotSum += Vector3.Dot(a, b);
                magB += b.sqrMagnitude;
            }
            
            if(magB < 1e-10) return 0f;

            return (float)Math.Max(0, Math.Min(1, dotSum / magB)); // clamp to [0,1]
        }
        
        public static float CompareBlendShapes(
            Mesh mesh,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            return CompareBlendShapes(ResolveAnalysisCache(mesh, null), referenceIndices, targetIndex, mode);
        }

        internal static float CompareBlendShapes(
            BlendShapeAnalysisCache cache,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            if (cache == null || referenceIndices == null || referenceIndices.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            float max = 0f;
            int count = 0;

            foreach (int referenceIndex in referenceIndices)
            {
                if (referenceIndex < 0)
                {
                    continue;
                }

                float score = CompareBlendShapes(cache, referenceIndex, targetIndex);
                sum += score;
                max = Mathf.Max(max, score);
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            return mode == BlendShapeComparisonMode.Max ? max : sum / count;
        }

        #region Math

        private static float ThresholdMap(float value, float lower = 0.05f, float upper = 0.95f)
        {
            if (value <= lower) return 0f;
            if (value >= upper) return 1f;

            
            return (float) Math.Round(value, 3);
        }
        private static bool Approximately(float a, float b, float epsilon = 1e-6f)
        {
            return Mathf.Abs(a - b) <= epsilon;
        }

        #endregion

        internal sealed class BackgroundBlendShapeSyncRequest
        {
            public NewActiveBlendShapeWeightMode CreationMode;
            public BlendShapeComparisonMode ComparisonMode = BlendShapeComparisonMode.Max;
            public Vector3[] Vertices;
            public double MeshReadMilliseconds;
            public int RequestedShapeCount;
            public Dictionary<int, Vector3[]> DeltaVerticesByShapeIndex = new();
            public List<int> EyeReferenceIndices = new();
            public List<int> MouthReferenceIndices = new();
            public List<BackgroundBlendShapeDefinitionRequest> DefinitionRequests = new();
            public List<BackgroundTargetShapeBlendDataRequest> TargetShapeRequests = new();
        }

        internal sealed class BackgroundBlendShapeDefinitionRequest
        {
            public string ShapeName;
            public int ShapeIndex;
        }

        internal sealed class BackgroundTargetShapeBlendDataRequest
        {
            public int TargetShapeArrayIndex;
            public string TargetShapeName;
            public float TargetShapeWeight;
            public int MainShapeIndex;
            public List<BackgroundBlendDataRequest> MissingBlendData = new();
        }

        internal sealed class BackgroundBlendDataRequest
        {
            public string ShapeName;
            public int ShapeIndex;
        }

        internal sealed class BackgroundBlendShapeSyncResult
        {
            public double CalculationMilliseconds;
            public List<BlendShapeDefinition> Definitions = new();
            public List<BackgroundTargetShapeBlendDataResult> TargetShapeResults = new();
        }

        internal sealed class BackgroundTargetShapeBlendDataResult
        {
            public int TargetShapeArrayIndex;
            public string TargetShapeName;
            public float TargetShapeWeight;
            public List<BlendData> BlendData = new();
        }

        internal static BackgroundBlendShapeSyncResult AnalyzeBackgroundBlendShapeSync(
            BackgroundBlendShapeSyncRequest request)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            var result = new BackgroundBlendShapeSyncResult();
            if (request == null)
            {
                result.CalculationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            if (request.DefinitionRequests.Count > 0)
            {
                var definitions = new BlendShapeDefinition[request.DefinitionRequests.Count];
                Parallel.For(
                    0,
                    request.DefinitionRequests.Count,
                    i => definitions[i] = CreateBackgroundDefinition(request, request.DefinitionRequests[i]));

                for (int i = 0; i < definitions.Length; i++)
                {
                    if (definitions[i] != null)
                    {
                        result.Definitions.Add(definitions[i]);
                    }
                }
            }

            if (request.TargetShapeRequests.Count > 0)
            {
                var targetShapeResults = new BackgroundTargetShapeBlendDataResult[request.TargetShapeRequests.Count];
                Parallel.For(
                    0,
                    request.TargetShapeRequests.Count,
                    i => targetShapeResults[i] = CreateBackgroundTargetShapeResult(request, request.TargetShapeRequests[i]));

                for (int i = 0; i < targetShapeResults.Length; i++)
                {
                    BackgroundTargetShapeBlendDataResult targetShapeResult = targetShapeResults[i];
                    if (targetShapeResult != null && targetShapeResult.BlendData.Count > 0)
                    {
                        result.TargetShapeResults.Add(targetShapeResult);
                    }
                }
            }

            stopwatch.Stop();
            result.CalculationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        private static BackgroundTargetShapeBlendDataResult CreateBackgroundTargetShapeResult(
            BackgroundBlendShapeSyncRequest request,
            BackgroundTargetShapeBlendDataRequest targetShapeRequest)
        {
            if (request == null || targetShapeRequest == null || targetShapeRequest.MissingBlendData.Count == 0)
            {
                return null;
            }

            var result = new BackgroundTargetShapeBlendDataResult
            {
                TargetShapeArrayIndex = targetShapeRequest.TargetShapeArrayIndex,
                TargetShapeName = targetShapeRequest.TargetShapeName,
                TargetShapeWeight = targetShapeRequest.TargetShapeWeight
            };

            foreach (BackgroundBlendDataRequest blendDataRequest in targetShapeRequest.MissingBlendData)
            {
                BlendData blendData = CreateBackgroundBlendData(
                    request,
                    blendDataRequest.ShapeName,
                    targetShapeRequest.MainShapeIndex,
                    blendDataRequest.ShapeIndex,
                    targetShapeRequest.TargetShapeWeight);
                if (blendData != null)
                {
                    result.BlendData.Add(blendData);
                }
            }

            return result.BlendData.Count > 0 ? result : null;
        }

        private static BlendData CreateBackgroundBlendData(
            BackgroundBlendShapeSyncRequest request,
            string targetShapeName,
            int mainShapeIndex,
            int targetShapeIndex,
            float mainWeight)
        {
            if (request == null || string.IsNullOrEmpty(targetShapeName))
            {
                return null;
            }

            if (request.CreationMode == NewActiveBlendShapeWeightMode.Zero)
            {
                return new BlendData
                {
                    m_TargetShapeName = targetShapeName,
                    m_Weight = 0f,
                    m_LeftWeight = 0f,
                    m_RightWeight = 0f,
                    m_SplitLeftRight = false
                };
            }

            var (left, right) = CompareBlendShapesLeftRight(request, mainShapeIndex, targetShapeIndex, mainWeight);
            left = ThresholdMap(left);
            right = ThresholdMap(right);

            return new BlendData
            {
                m_TargetShapeName = targetShapeName,
                m_Weight = (left + right) * 0.5f,
                m_LeftWeight = left,
                m_RightWeight = right,
                m_SplitLeftRight = !Approximately(left, right, 0.1f)
            };
        }

        private static BlendShapeDefinition CreateBackgroundDefinition(
            BackgroundBlendShapeSyncRequest request,
            BackgroundBlendShapeDefinitionRequest definitionRequest)
        {
            if (request == null || definitionRequest == null || string.IsNullOrEmpty(definitionRequest.ShapeName))
            {
                return null;
            }

            if (request.CreationMode == NewActiveBlendShapeWeightMode.Zero)
            {
                return new BlendShapeDefinition
                {
                    m_BlendShapeName = definitionRequest.ShapeName,
                    m_LeftEyeWeight = 0f,
                    m_RightEyeWeight = 0f,
                    m_MouthWeight = 0f
                };
            }

            if (request.EyeReferenceIndices.Count == 0 || request.MouthReferenceIndices.Count == 0)
            {
                return null;
            }

            (float leftEye, float rightEye) = CompareBlendShapesLeftRight(
                request,
                request.EyeReferenceIndices,
                definitionRequest.ShapeIndex,
                request.ComparisonMode);
            float mouth = CompareBlendShapes(
                request,
                request.MouthReferenceIndices,
                definitionRequest.ShapeIndex,
                request.ComparisonMode);

            return new BlendShapeDefinition
            {
                m_BlendShapeName = definitionRequest.ShapeName,
                m_LeftEyeWeight = ThresholdMap(leftEye),
                m_RightEyeWeight = ThresholdMap(rightEye),
                m_MouthWeight = ThresholdMap(mouth)
            };
        }

        private static bool TryGetDeltaVertices(
            BackgroundBlendShapeSyncRequest request,
            int shapeIndex,
            out Vector3[] deltaVertices)
        {
            if (request == null || request.DeltaVerticesByShapeIndex == null)
            {
                deltaVertices = null;
                return false;
            }

            return request.DeltaVerticesByShapeIndex.TryGetValue(shapeIndex, out deltaVertices);
        }

        private static (float leftScore, float rightScore) CompareBlendShapesLeftRight(
            BackgroundBlendShapeSyncRequest request,
            int indexA,
            int indexB,
            float weightA = 1f)
        {
            if (indexA == indexB)
            {
                return (1f, 1f);
            }

            if (request == null ||
                request.Vertices == null ||
                request.Vertices.Length == 0 ||
                !TryGetDeltaVertices(request, indexA, out Vector3[] deltaA) ||
                !TryGetDeltaVertices(request, indexB, out Vector3[] deltaB))
            {
                return (0f, 0f);
            }

            double dotLeft = 0;
            double magBLeft = 0;
            double dotRight = 0;
            double magBRight = 0;
            Vector3[] vertices = request.Vertices;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                float current = Vector3.Dot(a, b);
                if (vertices[i].x < 0f)
                {
                    dotLeft += current > 0 ? current : -0.5 * current;
                    magBLeft += b.sqrMagnitude;
                }
                else
                {
                    dotRight += current > 0 ? current : -0.5 * current;
                    magBRight += b.sqrMagnitude;
                }
            }

            float leftScore = (float)(magBLeft < 1e-10 ? 0 : Math.Max(0, Math.Min(1, dotLeft / magBLeft)));
            float rightScore = (float)(magBRight < 1e-10 ? 0 : Math.Max(0, Math.Min(1, dotRight / magBRight)));
            float factor = 2 * weightA - 1;

            return (leftScore * factor, rightScore * factor);
        }

        private static (float leftScore, float rightScore) CompareBlendShapesLeftRight(
            BackgroundBlendShapeSyncRequest request,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            if (request == null || referenceIndices == null || referenceIndices.Count == 0)
            {
                return (0f, 0f);
            }

            float leftSum = 0f;
            float rightSum = 0f;
            float leftMax = 0f;
            float rightMax = 0f;
            int count = 0;

            foreach (int referenceIndex in referenceIndices)
            {
                if (referenceIndex < 0)
                {
                    continue;
                }

                (float leftScore, float rightScore) = CompareBlendShapesLeftRight(request, referenceIndex, targetIndex);
                leftSum += leftScore;
                rightSum += rightScore;
                leftMax = Mathf.Max(leftMax, leftScore);
                rightMax = Mathf.Max(rightMax, rightScore);
                count++;
            }

            if (count == 0)
            {
                return (0f, 0f);
            }

            return mode == BlendShapeComparisonMode.Max
                ? (leftMax, rightMax)
                : (leftSum / count, rightSum / count);
        }

        private static float CompareBlendShapes(
            BackgroundBlendShapeSyncRequest request,
            int indexA,
            int indexB)
        {
            if (indexA == indexB)
            {
                return 1f;
            }

            if (request == null ||
                request.Vertices == null ||
                request.Vertices.Length == 0 ||
                !TryGetDeltaVertices(request, indexA, out Vector3[] deltaA) ||
                !TryGetDeltaVertices(request, indexB, out Vector3[] deltaB))
            {
                return 0f;
            }

            double dotSum = 0;
            double magB = 0;
            for (int i = 0; i < request.Vertices.Length; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                dotSum += Vector3.Dot(a, b);
                magB += b.sqrMagnitude;
            }

            if (magB < 1e-10)
            {
                return 0f;
            }

            return (float)Math.Max(0, Math.Min(1, dotSum / magB));
        }

        private static float CompareBlendShapes(
            BackgroundBlendShapeSyncRequest request,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            if (request == null || referenceIndices == null || referenceIndices.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            float max = 0f;
            int count = 0;

            foreach (int referenceIndex in referenceIndices)
            {
                if (referenceIndex < 0)
                {
                    continue;
                }

                float score = CompareBlendShapes(request, referenceIndex, targetIndex);
                sum += score;
                max = Mathf.Max(max, score);
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            return mode == BlendShapeComparisonMode.Max ? max : sum / count;
        }


        
        internal static IEnumerable<string> GetBlendShapesFromType(IEnumerable<TargetShape> shapes, ShapeType type)
        {
            if (shapes == null)
            {
                yield break;
            }

            foreach (var shape in shapes)
            {
                if (shape == null ||
                    shape.m_TargetShapeType != type ||
                    string.IsNullOrEmpty(shape.m_TargetShapeName))
                {
                    continue;
                }

                yield return shape.m_TargetShapeName;
            }
        }

        internal static List<int> GetBlendShapeIndices(Mesh mesh, IEnumerable<string> names)
        {
            var indices = new List<int>();
            if (mesh == null || names == null)
            {
                return indices;
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                int index = mesh.GetBlendShapeIndex(name);
                if (index >= 0 && seen.Add(index))
                {
                    indices.Add(index);
                }
            }

            return indices;
        }
        
        internal static BlendData CreateBlendData(SkinnedMeshRenderer smr, int mainShapeIndex, int targetShapeIndex, float mainWeight)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null) return null;

            return CreateBlendData(new BlendShapeAnalysisCache(mesh), smr, mainShapeIndex, targetShapeIndex, mainWeight);
        }

        internal static BlendData CreateBlendData(
            BlendShapeAnalysisCache cache,
            SkinnedMeshRenderer smr,
            int mainShapeIndex,
            int targetShapeIndex,
            float mainWeight)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
            {
                return null;
            }

            cache = ResolveAnalysisCache(mesh, cache);
            var (left, right) = CompareBlendShapesLeftRight(cache, mainShapeIndex, targetShapeIndex, mainWeight);

            left = ThresholdMap(left);
            right = ThresholdMap(right);

            return new BlendData()
            {
                m_TargetShapeName = mesh.GetBlendShapeName(targetShapeIndex),
                //m_TargetShapeIndex = targetShapeIndex,
                m_Weight = (left + right) * 0.5f,
                m_LeftWeight = left,
                m_RightWeight = right,
                m_SplitLeftRight = !Approximately(left, right, 0.1f)
            };
        }

        internal static BlendData CreateZeroBlendData(Mesh mesh, int targetShapeIndex)
        {
            if (mesh == null ||
                targetShapeIndex < 0 ||
                targetShapeIndex >= mesh.blendShapeCount)
            {
                return null;
            }

            return new BlendData
            {
                m_TargetShapeName = mesh.GetBlendShapeName(targetShapeIndex),
                m_Weight = 0f,
                m_LeftWeight = 0f,
                m_RightWeight = 0f,
                m_SplitLeftRight = false
            };
        }

        internal static BlendData CreateDefaultBlendData(
            SkinnedMeshRenderer smr,
            int mainShapeIndex,
            int targetShapeIndex,
            float mainWeight,
            NewActiveBlendShapeWeightMode creationMode)
        {
            return CreateDefaultBlendData(null, smr, mainShapeIndex, targetShapeIndex, mainWeight, creationMode);
        }

        internal static BlendData CreateDefaultBlendData(
            BlendShapeAnalysisCache cache,
            SkinnedMeshRenderer smr,
            int mainShapeIndex,
            int targetShapeIndex,
            float mainWeight,
            NewActiveBlendShapeWeightMode creationMode)
        {
            if (creationMode == NewActiveBlendShapeWeightMode.Zero)
            {
                return CreateZeroBlendData(smr?.sharedMesh, targetShapeIndex);
            }

            return CreateBlendData(cache, smr, mainShapeIndex, targetShapeIndex, mainWeight);
        }
        
        
        // internal static BlendShapeDefinition CreateDefinition(SkinnedMeshRenderer smr, int shapeIndex, 
        //     int referenceBlinkIndex, int referenceMouthIndex)
        // {
        //     return CreateDefinition(
        //         smr,
        //         shapeIndex,
        //         new[] { referenceBlinkIndex },
        //         new[] { referenceMouthIndex },
        //         BlendShapeComparisonMode.Max);
        // }

        internal static BlendShapeDefinition CreateDefinition(
            SkinnedMeshRenderer smr,
            int shapeIndex,
            IReadOnlyList<int> eyeReferenceIndices,
            IReadOnlyList<int> mouthReferenceIndices,
            BlendShapeComparisonMode mode)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
            {
                return null;
            }

            return CreateDefinition(new BlendShapeAnalysisCache(mesh), smr, shapeIndex, eyeReferenceIndices, mouthReferenceIndices, mode);
        }

        internal static BlendShapeDefinition CreateDefinition(
            BlendShapeAnalysisCache cache,
            SkinnedMeshRenderer smr,
            int shapeIndex,
            IReadOnlyList<int> eyeReferenceIndices,
            IReadOnlyList<int> mouthReferenceIndices,
            BlendShapeComparisonMode mode)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null ||
                shapeIndex < 0 ||
                shapeIndex >= mesh.blendShapeCount)
            {
                return null;
            }
            
            float currentWeight = smr.GetBlendShapeWeight(shapeIndex);
            if (Mathf.Approximately(currentWeight, 0f))
            {
                return null;
            }

            cache = ResolveAnalysisCache(mesh, cache);
            string name = mesh.GetBlendShapeName(shapeIndex);
            (float scoreL, float scoreR) = CompareBlendShapesLeftRight(cache, eyeReferenceIndices, shapeIndex, mode);
            float scoreMouth = CompareBlendShapes(cache, mouthReferenceIndices, shapeIndex, mode);

            return new BlendShapeDefinition
            {
                m_BlendShapeName = name,
                m_LeftEyeWeight = ThresholdMap(scoreL),
                m_RightEyeWeight = ThresholdMap(scoreR),
                m_MouthWeight = ThresholdMap(scoreMouth)
            };
        }

        internal static BlendShapeDefinition CreateZeroDefinition(Mesh mesh, int shapeIndex)
        {
            if (mesh == null ||
                shapeIndex < 0 ||
                shapeIndex >= mesh.blendShapeCount)
            {
                return null;
            }

            return new BlendShapeDefinition
            {
                m_BlendShapeName = mesh.GetBlendShapeName(shapeIndex),
                m_LeftEyeWeight = 0f,
                m_RightEyeWeight = 0f,
                m_MouthWeight = 0f
            };
        }

        internal static BlendShapeDefinition CreateDefaultDefinition(
            SkinnedMeshRenderer smr,
            int shapeIndex,
            IReadOnlyList<int> eyeReferenceIndices,
            IReadOnlyList<int> mouthReferenceIndices,
            BlendShapeComparisonMode comparisonMode,
            NewActiveBlendShapeWeightMode creationMode)
        {
            return CreateDefaultDefinition(null, smr, shapeIndex, eyeReferenceIndices, mouthReferenceIndices, comparisonMode, creationMode);
        }

        internal static BlendShapeDefinition CreateDefaultDefinition(
            BlendShapeAnalysisCache cache,
            SkinnedMeshRenderer smr,
            int shapeIndex,
            IReadOnlyList<int> eyeReferenceIndices,
            IReadOnlyList<int> mouthReferenceIndices,
            BlendShapeComparisonMode comparisonMode,
            NewActiveBlendShapeWeightMode creationMode)
        {
            if (creationMode == NewActiveBlendShapeWeightMode.Zero)
            {
                return CreateZeroDefinition(smr?.sharedMesh, shapeIndex);
            }

            return CreateDefinition(
                cache,
                smr,
                shapeIndex,
                eyeReferenceIndices,
                mouthReferenceIndices,
                comparisonMode);
        }
        
        
        internal static bool EnsureBlendDataForActiveShapes(
            FaceBlendShapeFixComponent component,
            IReadOnlyCollection<string> newActiveBlendShapes,
            NewActiveBlendShapeWeightMode creationMode)
        {
            if (component == null || component.TargetRenderer == null || component.TargetRenderer.sharedMesh == null)
                return false;
            if (newActiveBlendShapes.Count == 0)
            {
                return false;
            }
            
            var smr = component.TargetRenderer;
            bool addedBlendData = false;
            var analysisCache = new BlendShapeAnalysisCache(smr.sharedMesh);
            
            foreach (var targetShape in component.m_TargetShapes ?? Enumerable.Empty<TargetShape>())
            {
                addedBlendData |= EnsureBlendDataForActiveShapes(targetShape, newActiveBlendShapes, smr, creationMode, analysisCache);
            }

            if (addedBlendData)
            {
                EditorUtility.SetDirty(component);
            }
            return addedBlendData;
        }
        
        
        internal static bool EnsureBlendDataForActiveShapes(
            TargetShape targetShape,
            IReadOnlyCollection<string> newActiveBlendShapes,
            SkinnedMeshRenderer smr,
            NewActiveBlendShapeWeightMode creationMode)
        {
            return EnsureBlendDataForActiveShapes(targetShape, newActiveBlendShapes, smr, creationMode, null);
        }

        internal static bool EnsureBlendDataForActiveShapes(
            TargetShape targetShape,
            IReadOnlyCollection<string> newActiveBlendShapes,
            SkinnedMeshRenderer smr,
            NewActiveBlendShapeWeightMode creationMode,
            BlendShapeAnalysisCache cache)
        {
            if (targetShape == null || smr == null || smr.sharedMesh == null)
            {
                return false;
            }

            var mesh = smr.sharedMesh;

            int mainShapeIndex = mesh.GetBlendShapeIndex(targetShape.m_TargetShapeName);
            if (mainShapeIndex < 0)
            {
                return false;
            }

            var list = targetShape.m_BlendData?.ToList() ?? new List<BlendData>();
            var existingBlendShapeNames = new HashSet<string>(
                list.Where(b => b != null && !string.IsNullOrEmpty(b.m_TargetShapeName))
                    .Select(b => b.m_TargetShapeName));
            bool updatedShape = false;
            cache = ResolveAnalysisCache(mesh, cache);

            foreach (var blendShapeName in newActiveBlendShapes)
            {
                if (string.IsNullOrEmpty(blendShapeName) || existingBlendShapeNames.Contains(blendShapeName))
                {
                    continue;
                }

                int targetBlendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
                if (targetBlendShapeIndex < 0)
                {
                    continue;
                }

                var blendData = CreateDefaultBlendData(
                    cache,
                    smr,
                    mainShapeIndex,
                    targetBlendShapeIndex,
                    targetShape.m_Weight,
                    creationMode);
                if (blendData == null)
                {
                    continue;
                }
                    
                list.Add(blendData);
                existingBlendShapeNames.Add(blendShapeName);
                updatedShape = true;
            }
            
            if (updatedShape)
            {
                targetShape.m_BlendData = list.ToArray();
            }
            
            return updatedShape;
        }
        
    }
}
