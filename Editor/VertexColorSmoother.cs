using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.Collections;
using VRC.Collections.LowLevel.Unsafe;

namespace GeoTetra.GTAvaUtil
{
    public class VertexColorSmoother : IDisposable
    {
        readonly Mesh m_Mesh;
        NativeArray<Vector3> m_Vertices;
        NativeMultiHashMap<int, int> m_OverlappingVertIndices;
        NativeMultiHashMap<int, int> m_VertexTriangles;
        NativeHashMap<int, int> m_VertexSubMeshes;

        NativeArray<Color> m_ReadColors;
        NativeHashMap<int, Color> m_WriteColors;
        NativeArray<SubMesh> m_SubMeshes;

        CalculateOverlappingVertsJob m_CalculateOverlappingVertsJob;
        AverageVertexColors m_AverageVertexColors;

        JobHandle m_CalculateOverlappingVertsJobHandle;
        JobHandle m_AverageVertexColorsJobHandle;

        [Serializable]
        public struct SubMesh
        {
            public SubMeshDescriptor Descriptor;
            public UnsafeList<int> Triangles;

            public SubMesh(SubMeshDescriptor descriptor, UnsafeList<int> triangles)
            {
                Descriptor = descriptor;
                Triangles = triangles;
            }

            public void Dispose()
            {
                Triangles.Dispose();
            }
        }

        public VertexColorSmoother(Mesh mesh)
        {
            m_Mesh = mesh;

            if (m_Mesh == null)
            {
                Debug.LogWarning($"{m_Mesh} did not have necessary renderer or mesh.");
                return;
            }

            m_Vertices = new NativeArray<Vector3>(m_Mesh.vertices, Allocator.Persistent);
            m_ReadColors = new NativeArray<Color>(m_Mesh.colors, Allocator.Persistent);
            m_WriteColors = new NativeHashMap<int, Color>(m_Mesh.vertexCount, Allocator.Persistent);
            int triIndexCount = m_Mesh.triangles.Length;

            m_OverlappingVertIndices = new NativeMultiHashMap<int, int>(m_Mesh.vertexCount, Allocator.Persistent);
            m_VertexTriangles = new NativeMultiHashMap<int, int>(triIndexCount, Allocator.Persistent);
            m_VertexSubMeshes = new NativeHashMap<int, int>(m_Mesh.vertexCount, Allocator.Persistent);
            m_SubMeshes = new NativeArray<SubMesh>(m_Mesh.subMeshCount, Allocator.Persistent);
            List<int> triangles = new List<int>();
            for (int submeshIndex = 0; submeshIndex < m_Mesh.subMeshCount; ++submeshIndex)
            {
                triangles.Clear();
                var descriptor = m_Mesh.GetSubMesh(submeshIndex);
                m_Mesh.GetIndices(triangles, submeshIndex);
                UnsafeList<int> unsafeTris = new UnsafeList<int>(triangles.Count, Allocator.Persistent);
                for (int triIndex = 0; triIndex < triangles.Count; ++triIndex)
                {
                    int vertIndex = triangles[triIndex];
                    m_VertexSubMeshes.TryAdd(vertIndex, submeshIndex);
                    m_VertexTriangles.Add(vertIndex, triIndex);
                    unsafeTris.Add(vertIndex);
                }

                m_SubMeshes[submeshIndex] = new SubMesh(descriptor, unsafeTris);
            }
        }

        public IEnumerator RunCoroutine()
        {
            if (m_Mesh != null)
                yield return AverageColors();
        }

        public void Dispose()
        {
            m_Vertices.Dispose();
            m_ReadColors.Dispose();
            m_WriteColors.Dispose();

            m_OverlappingVertIndices.Dispose();
            m_VertexTriangles.Dispose();
            m_VertexSubMeshes.Dispose();

            for (int i = 0; i < m_SubMeshes.Length; ++i)
                m_SubMeshes[i].Dispose();

            m_SubMeshes.Dispose();
            
            EditorUtility.ClearProgressBar();
        }

        [BurstCompile]
        struct CalculateOverlappingVertsJob : IJobParallelFor
        {
            [ReadOnly] public float SearchDistance;

            [ReadOnly] public NativeArray<Vector3> Vertices;

            [WriteOnly] public NativeMultiHashMap<int, int>.ParallelWriter OverlappingVertIndices;

            public void Execute(int vertIndex)
            {
                for (int searchIndex = 0; searchIndex < Vertices.Length; ++searchIndex)
                {
                    if ((Vertices[vertIndex] - Vertices[searchIndex]).sqrMagnitude < SearchDistance &&
                        searchIndex != vertIndex)
                    {
                        OverlappingVertIndices.Add(vertIndex, searchIndex);
                    }
                }
            }
        }
        
        [BurstCompile]
        struct AverageVertexColors : IJobParallelFor
        {
            [ReadOnly] public NativeMultiHashMap<int, int> OverlappingVertIndices;

            [ReadOnly] public NativeMultiHashMap<int, int> VertexTriangles;

            [ReadOnly] public NativeHashMap<int, int> VertexSubMeshes;

            [ReadOnly] public NativeArray<SubMesh> SubMeshes;

            [ReadOnly] public NativeArray<Color> ReadColors;

            [WriteOnly] public NativeHashMap<int, Color>.ParallelWriter WriteColors;

            public void Execute(int vertIndex)
            {
                Color averagedColor = ReadColors[vertIndex];
                int totalCount = 1;

                AddVertColors(vertIndex, ref averagedColor, ref totalCount);

                if (totalCount > 0)
                {
                    averagedColor /= totalCount;
                    WriteColors.TryAdd(vertIndex, averagedColor);

                    foreach (var overlappedIndex in OverlappingVertIndices.GetValuesForKey(vertIndex))
                    {
                        WriteColors.TryAdd(overlappedIndex, averagedColor);
                    }
                }
            }

            void AddVertColors(int vertIndex, ref Color averagedColor, ref int totalFlowCount)
            {
                foreach (var triIndex in VertexTriangles.GetValuesForKey(vertIndex))
                {
                    AverageTriIndexColors(vertIndex, triIndex, VertexSubMeshes[vertIndex], ref averagedColor, ref totalFlowCount);
                }

                foreach (var overlapVertIndex in OverlappingVertIndices.GetValuesForKey(vertIndex))
                {
                    foreach (var triIndex in VertexTriangles.GetValuesForKey(overlapVertIndex))
                    {
                        AverageTriIndexColors(vertIndex, triIndex, VertexSubMeshes[overlapVertIndex], ref averagedColor, ref totalFlowCount);
                    }
                }
            }

            void AverageTriIndexColors(int vertIndex, int triIndex, int subMeshIndex, ref Color averagedFlow, ref int totalFlowCount)
            {
                int triIndexStartOffset = triIndex % 3;
                int triIndexStart = triIndex - triIndexStartOffset;

                for (int nextTriIndex = 0; nextTriIndex < 3; ++nextTriIndex)
                {
                    int neighborTriIndex = triIndexStart + nextTriIndex;
                    int neighborVertIndex = SubMeshes[subMeshIndex].Triangles[neighborTriIndex];
                    bool overlap = false;

                    foreach (var checkNeighborVertIndex in OverlappingVertIndices.GetValuesForKey(vertIndex))
                    {
                        if (checkNeighborVertIndex == neighborVertIndex)
                        {
                            overlap = true;
                        }
                    }

                    if (vertIndex != neighborVertIndex && !overlap)
                    {
                        averagedFlow += ReadColors[neighborVertIndex];
                        totalFlowCount++;
                    }
                }
            }
        }

        IEnumerator AverageColors()
        {
            EditorUtility.DisplayProgressBar("Calculating Overlapping Verts..", "", 0);
            
            m_CalculateOverlappingVertsJob = new CalculateOverlappingVertsJob
            {
                SearchDistance = float.Epsilon,
                Vertices = m_Vertices,
                OverlappingVertIndices = m_OverlappingVertIndices.AsParallelWriter()
            };
            m_CalculateOverlappingVertsJobHandle = m_CalculateOverlappingVertsJob.Schedule(m_Vertices.Length, 4);
            yield return new WaitUntil(() => m_CalculateOverlappingVertsJobHandle.IsCompleted);
            m_CalculateOverlappingVertsJobHandle.Complete();

            EditorUtility.DisplayProgressBar("Averaging Vertex Colors..", "", .2f);
            
            m_AverageVertexColors = new AverageVertexColors
            {
                ReadColors = m_ReadColors,
                WriteColors = m_WriteColors.AsParallelWriter(),
                SubMeshes = m_SubMeshes,
                OverlappingVertIndices = m_OverlappingVertIndices,
                VertexSubMeshes = m_VertexSubMeshes,
                VertexTriangles = m_VertexTriangles
            };
            m_AverageVertexColorsJobHandle = m_AverageVertexColors.Schedule(m_Vertices.Length, 4);
            yield return new WaitUntil(() => m_AverageVertexColorsJobHandle.IsCompleted);
            m_AverageVertexColorsJobHandle.Complete();

            var newColors = new Color[m_Mesh.vertexCount];
            for (int i = 0; i < newColors.Length; ++i)
            {
                newColors[i] = m_WriteColors[i];
                m_ReadColors[i] = m_WriteColors[i];
            }
            
            m_Mesh.colors = newColors;
            m_Mesh.UploadMeshData(false);

            EditorUtility.DisplayProgressBar("Averaging Vertex Colors..", "", 1);
            
        }
    }
}