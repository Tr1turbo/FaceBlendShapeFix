using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Triturbo.FaceBlendShapeFix
{
    internal static class PreviewMeshBuilder
    {
        public static Mesh BuildPreviewMesh(Mesh source, HashSet<string> inverseShapes, HashSet<string> additiveShapes, float smoothWidth)
        {
            if (source == null) return null;

            Mesh newMesh = Object.Instantiate(source);
            newMesh.name = source.name + "_Preview";

            for (int i = 0; i < source.blendShapeCount; i++)
            {
                string shapeName = source.GetBlendShapeName(i);

                if (additiveShapes.Contains(shapeName))
                {
                    SplitBlendShape(newMesh, i, smoothWidth, 1, ".add");
                }

                if (inverseShapes.Contains(shapeName))
                {
                    SplitBlendShape(newMesh, i, smoothWidth, -1, ".inv");
                }
            }
            return newMesh;
        }

        public static void SplitBlendShape(Mesh targetMesh, int targetIndex, float smoothWidth, float factor = -1, string suffix = ".inv")
        {
            int vertexCount = targetMesh.vertexCount;
            Vector3[] vertices = targetMesh.vertices;

            string shapeName = targetMesh.GetBlendShapeName(targetIndex);
            float frameWeight = targetMesh.GetBlendShapeFrameWeight(targetIndex, 0);

            Vector3[] deltaVertices = new Vector3[vertexCount];
            Vector3[] deltaNormals = new Vector3[vertexCount];
            Vector3[] deltaTangents = new Vector3[vertexCount];
            targetMesh.GetBlendShapeFrameVertices(targetIndex, 0, deltaVertices, deltaNormals, deltaTangents);

            Vector3[] leftVertices = new Vector3[vertexCount];
            Vector3[] leftNormals = new Vector3[vertexCount];
            Vector3[] leftTangents = new Vector3[vertexCount];

            Vector3[] rightVertices = new Vector3[vertexCount];
            Vector3[] rightNormals = new Vector3[vertexCount];
            Vector3[] rightTangents = new Vector3[vertexCount];

            for (int v = 0; v < vertexCount; v++)
            {
                var (leftFactor, rightFactor) = MeshBlendShapeProcessor.CalculateSideFactor(vertices[v].x, smoothWidth);
                leftVertices[v] = factor * leftFactor * deltaVertices[v];
                leftNormals[v] = factor * leftFactor * deltaNormals[v];
                leftTangents[v] = factor * leftFactor * deltaTangents[v];
                rightVertices[v] = factor * rightFactor * deltaVertices[v];
                rightNormals[v] = factor * rightFactor * deltaNormals[v];
                rightTangents[v] = factor * rightFactor * deltaTangents[v];
            }

            targetMesh.AddBlendShapeFrame(shapeName + $"{suffix}.L", frameWeight, leftVertices, leftNormals, leftTangents);
            targetMesh.AddBlendShapeFrame(shapeName + $"{suffix}.R", frameWeight, rightVertices, rightNormals, rightTangents);
        }
    }
}

