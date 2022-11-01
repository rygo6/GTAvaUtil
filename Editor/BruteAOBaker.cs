using System;
using System.Collections;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace GeoTetra.GTAvaUtil
{
    public class BruteAOBaker : IDisposable
    {
        ComputeShader m_ComputeShader;

        MeshFilter m_MeshFilter;
        float _AngleMin = .2f;
        float _AngleMax = .99f;
        int _HeightSteps = 8;
        int _RotationSteps = 8;
        float _SurfaceOffset = float.Epsilon;

        ComputeBuffer m_VerticesBuffer;
        ComputeBuffer m_AOVertsBuffer;
        ComputeBuffer m_IndicesBuffer;
        ComputeBuffer m_NormalsBuffer;
        ComputeBuffer m_TangentsBuffer;
        NativeArray<Vector3> m_Vertices;
        NativeArray<Vector3> m_Normals;
        NativeArray<Vector4> m_Tangents;
        NativeArray<int> m_Indices;
        int m_BruteAOVertBakeKernel;

        public BruteAOBaker(MeshFilter meshFilter)
        {
            var assetGuid = AssetDatabase.FindAssets("BruteAOBakerShader");
            var path = AssetDatabase.GUIDToAssetPath(assetGuid[0]);
            m_ComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

            m_MeshFilter = meshFilter;
            var mesh = m_MeshFilter.sharedMesh;

            m_Vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.Persistent);
            m_Normals = new NativeArray<Vector3>(mesh.normals, Allocator.Persistent);
            m_Tangents = new NativeArray<Vector4>(mesh.tangents, Allocator.Persistent);
            m_Indices = new NativeArray<int>(mesh.triangles, Allocator.Persistent);

            Debug.Assert(m_Vertices.Length > 0, "Mesh has no vertices?!");
            Debug.Assert(m_Normals.Length > 0, "Mesh has no normals! If not exported from blender, set mesh to generate them.");
            Debug.Assert(m_Tangents.Length > 0, "Mesh has no tangents! If not exported from blender, set mesh to generate them.");
            Debug.Assert(m_Indices.Length > 0, "Mesh has no indices?");

            m_VerticesBuffer = new ComputeBuffer(m_Vertices.Length, sizeof(float) * 3);
            m_NormalsBuffer = new ComputeBuffer(m_Normals.Length, sizeof(float) * 3);
            m_TangentsBuffer = new ComputeBuffer(m_Tangents.Length, sizeof(float) * 4);
            m_AOVertsBuffer = new ComputeBuffer(m_Vertices.Length, sizeof(float));
            m_IndicesBuffer = new ComputeBuffer(m_Indices.Length, sizeof(int));

            m_VerticesBuffer.SetData(m_Vertices);
            m_NormalsBuffer.SetData(m_Normals);
            m_TangentsBuffer.SetData(m_Tangents);
            m_IndicesBuffer.SetData(m_Indices);
            
            m_BruteAOVertBakeKernel = m_ComputeShader.FindKernel("BruteAOVertBake");
        }

        public void Dispose()
        {
            m_Vertices.Dispose();
            m_Indices.Dispose();
            m_Normals.Dispose();
            m_Tangents.Dispose();

            EditorUtility.ClearProgressBar();
        }

        public IEnumerator RunCoroutine()
        {
            yield return Bake();
        }

        IEnumerator Bake()
        {
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_vertices", m_VerticesBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_normals", m_NormalsBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_tangents", m_TangentsBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_indices", m_IndicesBuffer);
            m_ComputeShader.SetBuffer(m_BruteAOVertBakeKernel, "_aoVertDist", m_AOVertsBuffer);
            m_ComputeShader.SetInt("_VertLength", m_Vertices.Length);
            m_ComputeShader.SetInt("_IndicesLength", m_Indices.Length);
            m_ComputeShader.SetMatrix("unity_ObjectToWorld", m_MeshFilter.transform.localToWorldMatrix);
            m_ComputeShader.SetMatrix("unity_WorldToObject", m_MeshFilter.transform.worldToLocalMatrix);
            m_ComputeShader.SetFloat("_SurfaceOffset", _SurfaceOffset);

            Color[] colorArray = new Color[m_Vertices.Length];
            float[] aoArray = new float[m_Vertices.Length];
            float aoSampleCount = 0;

            int count = 0;
            for (int h = 0; h < _HeightSteps; ++h)
            {
                float heightAngle = Mathf.Lerp(_AngleMin, _AngleMax, (1f / _HeightSteps) * h);
                m_ComputeShader.SetFloat("_HeightAngle", heightAngle);

                for (int r = 0; r < _RotationSteps; ++r)
                {
                    count++;

                    float yAngle = (1f / _RotationSteps) * r;
                    m_ComputeShader.SetFloat("_YAngle", yAngle);

                    int vertThreadGroups = Mathf.CeilToInt((float)m_Vertices.Length / 64f);
                    m_ComputeShader.Dispatch(m_BruteAOVertBakeKernel, vertThreadGroups, 1, 1);

                    m_AOVertsBuffer.GetData(aoArray);

                    for (int i = 0; i < m_Vertices.Length; ++i)
                    {
                        // rolling average
                        colorArray[i].r = ((colorArray[i].r * (count - 1)) + aoArray[i]) / count;
                        colorArray[i].g = ((colorArray[i].g * (count - 1)) + aoArray[i]) / count;
                        colorArray[i].b = ((colorArray[i].b * (count - 1)) + aoArray[i]) / count;
                        colorArray[i].a = 1;
                    }

                    m_MeshFilter.sharedMesh.colors = colorArray;
                    m_MeshFilter.sharedMesh.UploadMeshData(false);

                    EditorUtility.DisplayProgressBar("Baking Vertex Colors..", "", (float)count / (_HeightSteps * _RotationSteps));
                    yield return null;
                }
            }
        }
    }
}