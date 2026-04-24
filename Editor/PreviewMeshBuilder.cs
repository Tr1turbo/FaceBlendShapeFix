using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class PreviewMeshBuilder
    {
        private readonly struct SplitBuildStats
        {
            public readonly int InverseSplitCount;
            public readonly int AdditiveSplitCount;

            public SplitBuildStats(int inverseSplitCount, int additiveSplitCount)
            {
                InverseSplitCount = inverseSplitCount;
                AdditiveSplitCount = additiveSplitCount;
            }
        }

        private sealed class SplitBuffers
        {
            public readonly Vector3[] DeltaVertices;
            public readonly Vector3[] DeltaNormals;
            public readonly Vector3[] DeltaTangents;
            public readonly Vector3[] LeftVertices;
            public readonly Vector3[] LeftNormals;
            public readonly Vector3[] LeftTangents;
            public readonly Vector3[] RightVertices;
            public readonly Vector3[] RightNormals;
            public readonly Vector3[] RightTangents;

            public SplitBuffers(int vertexCount)
            {
                DeltaVertices = new Vector3[vertexCount];
                DeltaNormals = new Vector3[vertexCount];
                DeltaTangents = new Vector3[vertexCount];
                LeftVertices = new Vector3[vertexCount];
                LeftNormals = new Vector3[vertexCount];
                LeftTangents = new Vector3[vertexCount];
                RightVertices = new Vector3[vertexCount];
                RightNormals = new Vector3[vertexCount];
                RightTangents = new Vector3[vertexCount];
            }
        }

        public static Mesh BuildPreviewMesh(Mesh source, HashSet<string> inverseShapes, HashSet<string> additiveShapes, float smoothWidth)
        {
            if (source == null) return null;

            Stopwatch stopwatch = Stopwatch.StartNew();
            Mesh newMesh = Object.Instantiate(source);
            newMesh.name = source.name + "_Preview";
            SplitBuildStats stats = AppendPreviewShapesInternal(source, newMesh, inverseShapes, additiveShapes, smoothWidth);

            stopwatch.Stop();
            FaceBlendShapeFixDiagnostics.LogIfSlow(
                "Build preview mesh",
                stopwatch.Elapsed.TotalMilliseconds,
                $"mesh={source.name}, vertices={source.vertexCount}, sourceBlendShapes={source.blendShapeCount}, " +
                $"inverseShapes={inverseShapes.Count}, additiveShapes={additiveShapes.Count}, " +
                $"inverseSplits={stats.InverseSplitCount}, additiveSplits={stats.AdditiveSplitCount}",
                thresholdMilliseconds: 4d);
            return newMesh;
        }

        public static void AppendPreviewShapes(
            Mesh source,
            Mesh target,
            HashSet<string> inverseShapes,
            HashSet<string> additiveShapes,
            float smoothWidth)
        {
            if (source == null || target == null)
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            SplitBuildStats stats = AppendPreviewShapesInternal(source, target, inverseShapes, additiveShapes, smoothWidth);
            stopwatch.Stop();
            FaceBlendShapeFixDiagnostics.LogIfSlow(
                "Add shape to preview mesh",
                stopwatch.Elapsed.TotalMilliseconds,
                $"mesh={source.name}, vertices={source.vertexCount}, sourceBlendShapes={source.blendShapeCount}, " +
                $"inverseShapes={inverseShapes.Count}, additiveShapes={additiveShapes.Count}, " +
                $"inverseSplits={stats.InverseSplitCount}, additiveSplits={stats.AdditiveSplitCount}",
                thresholdMilliseconds: 4d);
        }

        private static SplitBuildStats AppendPreviewShapesInternal(
            Mesh source,
            Mesh target,
            HashSet<string> inverseShapes,
            HashSet<string> additiveShapes,
            float smoothWidth)
        {
            Vector3[] sourceVertices = source.vertices;
            var splitBuffers = new SplitBuffers(source.vertexCount);
            int inverseSplitCount = 0;
            int additiveSplitCount = 0;

            for (int i = 0; i < source.blendShapeCount; i++)
            {
                string shapeName = source.GetBlendShapeName(i);

                if (additiveShapes.Contains(shapeName))
                {
                    SplitBlendShape(source, target, sourceVertices, splitBuffers, i, smoothWidth, 1f, ".add");
                    additiveSplitCount++;
                }

                if (inverseShapes.Contains(shapeName))
                {
                    SplitBlendShape(source, target, sourceVertices, splitBuffers, i, smoothWidth, -1f, ".inv");
                    inverseSplitCount++;
                }
            }

            return new SplitBuildStats(inverseSplitCount, additiveSplitCount);
        }

        private static void SplitBlendShape(
            Mesh sourceMesh,
            Mesh targetMesh,
            Vector3[] sourceVertices,
            SplitBuffers buffers,
            int targetIndex,
            float smoothWidth,
            float factor = -1f,
            string suffix = ".inv")
        {
            int vertexCount = sourceMesh.vertexCount;

            string shapeName = sourceMesh.GetBlendShapeName(targetIndex);
            float frameWeight = sourceMesh.GetBlendShapeFrameWeight(targetIndex, 0);

            sourceMesh.GetBlendShapeFrameVertices(
                targetIndex,
                0,
                buffers.DeltaVertices,
                buffers.DeltaNormals,
                buffers.DeltaTangents);

            for (int v = 0; v < vertexCount; v++)
            {
                var (leftFactor, rightFactor) = MeshBlendShapeProcessor.CalculateSideFactor(sourceVertices[v].x, smoothWidth);
                buffers.LeftVertices[v] = factor * leftFactor * buffers.DeltaVertices[v];
                buffers.LeftNormals[v] = factor * leftFactor * buffers.DeltaNormals[v];
                buffers.LeftTangents[v] = factor * leftFactor * buffers.DeltaTangents[v];
                buffers.RightVertices[v] = factor * rightFactor * buffers.DeltaVertices[v];
                buffers.RightNormals[v] = factor * rightFactor * buffers.DeltaNormals[v];
                buffers.RightTangents[v] = factor * rightFactor * buffers.DeltaTangents[v];
            }

            targetMesh.AddBlendShapeFrame(
                shapeName + $"{suffix}.L",
                frameWeight,
                buffers.LeftVertices,
                buffers.LeftNormals,
                buffers.LeftTangents);
            targetMesh.AddBlendShapeFrame(
                shapeName + $"{suffix}.R",
                frameWeight,
                buffers.RightVertices,
                buffers.RightNormals,
                buffers.RightTangents);
        }
    }
}
