using System;
using System.Collections;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace GeoTetra.GTAvaUtil
{
    public class BruteAOBaker : IDisposable
    {
        float m_AngleMin = .2f;
        float m_AngleMax = .8f;
        int m_HeightSteps = 10;
        int m_RotationSteps = 10;
        float m_SurfaceOffset = float.Epsilon;
        
        MeshFilter m_MeshFilter;
        MeshFilter m_FromMeshFilter;
        
        ComputeShader m_ComputeShader;

        ComputeBuffer m_FromVerticesBuffer;
        ComputeBuffer m_FromIndicesBuffer;
        
        ComputeBuffer m_VerticesBuffer;
        ComputeBuffer m_NormalsBuffer;
        ComputeBuffer m_TangentsBuffer;
        ComputeBuffer m_AOVertsBuffer;

        NativeArray<Vector3> m_FromVertices;
        NativeArray<int> m_FromIndices;

        NativeArray<Vector3> m_Vertices;
        NativeArray<Vector3> m_Normals;
        NativeArray<Vector4> m_Tangents;

        int m_BruteAOVertBakeKernel;
        bool m_bakeToAlpha;

        /// <summary>
        /// If FromMeshFilter != null then only that mesh will occlude.
        /// Otherwise meshFilter will self occlude
        /// </summary>
        public BruteAOBaker(MeshFilter meshFilter, MeshFilter fromMeshFilter, bool bakeToAlpha)
        {
            m_bakeToAlpha = bakeToAlpha;
            
            var assetGuid = AssetDatabase.FindAssets("BruteAOBakerShader");
            var path = AssetDatabase.GUIDToAssetPath(assetGuid[0]);
            m_ComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

            m_MeshFilter = meshFilter;
            m_FromMeshFilter = fromMeshFilter == null ? m_MeshFilter : fromMeshFilter;

            var mesh = m_MeshFilter.sharedMesh;
            var fromMesh = m_FromMeshFilter.sharedMesh;

            m_FromVertices = new NativeArray<Vector3>(fromMesh.vertices, Allocator.Persistent);
            m_FromIndices = new NativeArray<int>(fromMesh.triangles, Allocator.Persistent);
            
            m_Vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.Persistent);
            m_Normals = new NativeArray<Vector3>(mesh.normals, Allocator.Persistent);
            m_Tangents = new NativeArray<Vector4>(mesh.tangents, Allocator.Persistent);

            Debug.Assert(m_FromVertices.Length > 0, "Target Mesh has no vertices?!");
            Debug.Assert(m_FromIndices.Length > 0, "Target Mesh has no indices?");
            
            Debug.Assert(m_Vertices.Length > 0, "Mesh has no vertices?!");
            Debug.Assert(m_Normals.Length > 0, "Mesh has no normals! If not exported from blender, set mesh to generate them.");
            Debug.Assert(m_Tangents.Length > 0, "Mesh has no tangents! If not exported from blender, set mesh to generate them.");

            m_FromVerticesBuffer = new ComputeBuffer(m_FromVertices.Length, sizeof(float) * 3);
            m_FromIndicesBuffer = new ComputeBuffer(m_FromIndices.Length, sizeof(int));
            
            m_VerticesBuffer = new ComputeBuffer(m_Vertices.Length, sizeof(float) * 3);
            m_NormalsBuffer = new ComputeBuffer(m_Normals.Length, sizeof(float) * 3);
            m_TangentsBuffer = new ComputeBuffer(m_Tangents.Length, sizeof(float) * 4);
            m_AOVertsBuffer = new ComputeBuffer(m_Vertices.Length, sizeof(float));
            
            m_FromVerticesBuffer.SetData(m_FromVertices);
            m_FromIndicesBuffer.SetData(m_FromIndices);
            
            m_VerticesBuffer.SetData(m_Vertices);
            m_NormalsBuffer.SetData(m_Normals);
            m_TangentsBuffer.SetData(m_Tangents);

            m_BruteAOVertBakeKernel = m_ComputeShader.FindKernel("BruteAOVertBake");
        }

        public void Dispose()
        {
            m_FromVertices.Dispose();
            m_FromIndices.Dispose();
            m_Vertices.Dispose();
            m_Normals.Dispose();
            m_Tangents.Dispose();
            
            m_FromVerticesBuffer.Dispose();
            m_FromIndicesBuffer.Dispose();
            
            m_VerticesBuffer.Dispose();
            m_NormalsBuffer.Dispose();
            m_TangentsBuffer.Dispose();
            m_AOVertsBuffer.Dispose();
            
            EditorUtility.ClearProgressBar();
        }

        public IEnumerator RunCoroutine()
        {
            yield return Bake();
        }

        IEnumerator Bake()
        {
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_TargetVertices", m_FromVerticesBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_TargetIndices", m_FromIndicesBuffer);
            m_ComputeShader.SetInt("_TargetIndicesLength", m_FromIndices.Length);
            
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_Vertices", m_VerticesBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_Normals", m_NormalsBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_Tangents", m_TangentsBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_AoVertDist", m_AOVertsBuffer);
            m_ComputeShader.SetInt("_VertLength", m_Vertices.Length);
            m_ComputeShader.SetMatrix("unity_ObjectToWorld", m_MeshFilter.transform.localToWorldMatrix);
            m_ComputeShader.SetMatrix("unity_WorldToObject", m_MeshFilter.transform.worldToLocalMatrix);
            m_ComputeShader.SetFloat("_SurfaceOffset", m_SurfaceOffset);
            
            var colorArray = m_MeshFilter.sharedMesh.colors.Length > 0 ? 
                m_MeshFilter.sharedMesh.colors : 
                new Color[m_Vertices.Length];
            var aoArray = new float[m_Vertices.Length];

            var count = 0;
            for (int h = 0; h < m_HeightSteps; ++h)
            {
                float heightAngle = Mathf.Lerp(m_AngleMin, m_AngleMax, (1f / m_HeightSteps) * h);
                m_ComputeShader.SetFloat("_HeightAngle", heightAngle);

                for (int r = 0; r < m_RotationSteps; ++r)
                {
                    count++;

                    var yAngle = (1f / m_RotationSteps) * r;
                    m_ComputeShader.SetFloat("_YAngle", yAngle);

                    var vertThreadGroups = Mathf.CeilToInt((float)m_Vertices.Length / 64f);
                    m_ComputeShader.Dispatch(m_BruteAOVertBakeKernel, vertThreadGroups, 1, 1);

                    m_AOVertsBuffer.GetData(aoArray);

                    for (int i = 0; i < m_Vertices.Length; ++i)
                    {
                        if (m_bakeToAlpha)
                        {
                            if (i == 0)
                                colorArray[i].a = 0;
                            var rollingAverage = RollingAverage(colorArray[i].a, aoArray[i], count);
                            colorArray[i].a = rollingAverage;
                        }
                        else
                        {
                            if (i == 0)
                            {
                                colorArray[i].r = 0;
                                colorArray[i].g = 0;
                                colorArray[i].b = 0;
                            }
                            var rollingAverage = RollingAverage(colorArray[i].r, aoArray[i], count);
                            colorArray[i].r = rollingAverage;
                            colorArray[i].g = rollingAverage;
                            colorArray[i].b = rollingAverage;
                        }
                    }

                    m_MeshFilter.sharedMesh.colors = colorArray;
                    m_MeshFilter.sharedMesh.UploadMeshData(false);

                    EditorUtility.DisplayProgressBar("Baking Vertex Colors..", "", (float)count / (m_HeightSteps * m_RotationSteps));
                    yield return null;
                }
            }
        }

        float RollingAverage(float currentValue, float newValue, int count)
        {
            return ((currentValue * (count - 1)) + newValue) / count;
        }
    }
}