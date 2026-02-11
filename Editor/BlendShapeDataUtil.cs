using System;
using System.Collections.Generic;
using System.Linq;
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
        
        public static (float leftScore, float rightScore) CompareBlendShapesLeftRight(Mesh mesh, int indexA, int indexB, float weightA = 1f)
        {
            if (indexA == indexB) return (1f, 1f);

            int count = mesh.vertexCount;
            var deltaA = new Vector3[count];
            var deltaB = new Vector3[count];

            mesh.GetBlendShapeFrameVertices(indexA, 0, deltaA, null, null);
            mesh.GetBlendShapeFrameVertices(indexB, 0, deltaB, null, null);

            double dotLeft = 0;
            //double magALeft = 0;
            
            double magBLeft = 0;
            double dotRight = 0;
            //double magARight = 0;
            double magBRight = 0;

            var vertices = mesh.vertices;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                    continue;
                
                var current = Vector3.Dot(a, b);

                if (vertices[i].x < 0f)
                {
                    //dotLeft += Math.Abs(Vector3.Dot(a, b));
                    //dotLeft += Math.Max(0, Vector3.Dot(a, b));

                    dotLeft += current > 0 ? current: -0.5 * current;
                    
                    //magALeft += a.sqrMagnitude;
                    magBLeft += b.sqrMagnitude;
                }
                else
                {
                    //dotRight += Math.Abs(Vector3.Dot(a, b));
                    //dotRight += Math.Max(0, Vector3.Dot(a, b));

                    dotRight += current > 0 ? current : -0.5 * current;

                    //magARight += a.sqrMagnitude;
                    magBRight += b.sqrMagnitude;
                }
            }

            //float leftScore = (float)((magALeft * magBLeft < 1e-10) ? 0 : Math.Max(0, Math.Min(1, dotLeft / Math.Sqrt(magALeft * magBLeft))));
            //float rightScore = (float)((magARight * magBRight < 1e-10) ? 0 : Math.Max(0, Math.Min(1, dotRight / Math.Sqrt(magARight * magBRight))));

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
            if (mesh == null || referenceIndices == null || referenceIndices.Count == 0)
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

                (float leftScore, float rightScore) = CompareBlendShapesLeftRight(mesh, referenceIndex, targetIndex);
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
            if (indexA == indexB) return 1;
            
            int count = mesh.vertexCount;
            var deltaA = new Vector3[count];
            var deltaB = new Vector3[count];

            mesh.GetBlendShapeFrameVertices(indexA, 0, deltaA, null, null);
            mesh.GetBlendShapeFrameVertices(indexB, 0, deltaB, null, null);

            double dotSum = 0;
            double magA = 0;
            double magB = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 a = deltaA[i];
                Vector3 b = deltaB[i];

                if (a.sqrMagnitude < 1e-10f && b.sqrMagnitude < 1e-10f)
                    continue;

                dotSum += Vector3.Dot(a, b);
                magA += a.sqrMagnitude;
                magB += b.sqrMagnitude;
            }

            //double denom = Math.Sqrt(magA * magB);
            
            if(magB < 1e-10) return 0f;
            //if (denom < 1e-10) return 0f;

            return (float)Math.Max(0, Math.Min(1, dotSum / magB)); // clamp to [0,1]
        }
        
        public static float CompareBlendShapes(
            Mesh mesh,
            IReadOnlyList<int> referenceIndices,
            int targetIndex,
            BlendShapeComparisonMode mode)
        {
            if (mesh == null || referenceIndices == null || referenceIndices.Count == 0)
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

                float score = CompareBlendShapes(mesh, referenceIndex, targetIndex);
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
            
            var (left, right) = CompareBlendShapesLeftRight(mesh, mainShapeIndex, targetShapeIndex, mainWeight);

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

            string name = mesh.GetBlendShapeName(shapeIndex);
            (float scoreL, float scoreR) = CompareBlendShapesLeftRight(mesh, eyeReferenceIndices, shapeIndex, mode);
            float scoreMouth = CompareBlendShapes(mesh, mouthReferenceIndices, shapeIndex, mode);

            return new BlendShapeDefinition
            {
                m_BlendShapeName = name,
                m_LeftEyeWeight = ThresholdMap(scoreL),
                m_RightEyeWeight = ThresholdMap(scoreR),
                m_MouthWeight = ThresholdMap(scoreMouth)
            };
        }
        
        
        internal static bool EnsureBlendDataForActiveShapes(FaceBlendShapeFixComponent component, IReadOnlyCollection<string> newActiveBlendShapes)
        {
            if (component == null || component.TargetRenderer == null || component.TargetRenderer.sharedMesh == null)
                return false;
            if (newActiveBlendShapes.Count == 0)
            {
                return false;
            }
            
            var smr = component.TargetRenderer;
            bool addedBlendData = false;
            
            foreach (var targetShape in component.m_TargetShapes ?? Enumerable.Empty<TargetShape>())
            {
                addedBlendData |= EnsureBlendDataForActiveShapes(targetShape, newActiveBlendShapes, smr);
            }

            if (addedBlendData)
            {
                EditorUtility.SetDirty(component);
            }
            return addedBlendData;
        }
        
        
        internal static bool EnsureBlendDataForActiveShapes(TargetShape targetShape, IReadOnlyCollection<string> newActiveBlendShapes, SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;

            int mainShapeIndex = mesh.GetBlendShapeIndex(targetShape.m_TargetShapeName);
            if (mainShapeIndex < 0)
            {
                return false;
            }

            var list = targetShape.m_BlendData?.ToList() ?? new List<BlendData>();
            bool updatedShape = false;

            foreach (var blendShapeName in newActiveBlendShapes)
            {
                // Skip if already exists
                if (list.Any(b => b.m_TargetShapeName == blendShapeName))
                    continue;

                int targetBlendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
                if (targetBlendShapeIndex < 0)
                {
                    continue;
                }

                var blendData = CreateBlendData(smr, mainShapeIndex, targetBlendShapeIndex, targetShape.m_Weight);
                if (blendData == null)
                {
                    continue;
                }
                    
                list.Add(blendData);
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
