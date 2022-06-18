﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using BestHTTP.SecureProtocol.Org.BouncyCastle.Asn1;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace GeoTetra.GTAvaUtil
{
    public class MenuUtilites
    {
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Transfer SkinnedMeshRenderer Bones...", false)]
        static void SetBonesTo(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Select two SkinnedMeshRenderers.",
                    "Select the SkinnedMeshRender you want to transfer bones from, then the SkinnedMeshRenderer you wan to transfer bones to.",
                    "Ok");
            }
            
            if (Selection.objects.Length != 2)
            {
                ErrorDialogue();
                return;
            }

            SkinnedMeshRenderer sourceRenderer = (Selection.objects[0] as GameObject).GetComponent<SkinnedMeshRenderer>();
            SkinnedMeshRenderer destinationRenderer = (Selection.objects[1] as GameObject).GetComponent<SkinnedMeshRenderer>();
            if (sourceRenderer == null || destinationRenderer == null)
            {
                ErrorDialogue();
                return;
            }

            Dictionary<string, Transform> sourceBones = new Dictionary<string, Transform>();
            foreach (var bone in sourceRenderer.bones)
            {
                sourceBones.Add(bone.name, bone);
            }
            
            Matrix4x4[] newBindPoses = new Matrix4x4[destinationRenderer.bones.Length];
            Transform[] newBones = new Transform[destinationRenderer.bones.Length];
            for (int i = 0; i < destinationRenderer.bones.Length; ++i)
            {
                var destinationBone = destinationRenderer.bones[i];
                if (sourceBones.TryGetValue(destinationBone.name, out var newBone))
                {
                    newBones[i] = newBone;
                    newBindPoses[i] = newBone.worldToLocalMatrix * destinationRenderer.localToWorldMatrix;
                }
                else
                {
                    EditorUtility.DisplayDialog($"Couldn't find bone {destinationBone.name}!",
                        "In order to transfer bones, every bone in the new SkinnnedMeshRender must also be in the prior SkinnedMeshRender and named the same.",
                        "Ok");
                    return;
                }
            }
            
            Mesh newDestinationMesh = MonoBehaviour.Instantiate(destinationRenderer.sharedMesh);
            newDestinationMesh.bindposes = newBindPoses;
            string oldMeshPath = AssetDatabase.GetAssetPath(destinationRenderer.sharedMesh);
            var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, destinationRenderer.sharedMesh.name, "TransferredBones");
            AssetDatabase.CreateAsset(newDestinationMesh, encryptedMeshPath);
            AssetDatabase.SaveAssets();

            var newDestinationRenderer  = MonoBehaviour.Instantiate(destinationRenderer);
            Undo.RegisterCreatedObjectUndo(newDestinationRenderer.gameObject, "Transferred Bones.");
            newDestinationRenderer.bones = newBones;
            newDestinationRenderer.rootBone = sourceRenderer.rootBone;
            newDestinationRenderer.sharedMesh = newDestinationMesh;
            newDestinationRenderer.transform.SetParent(sourceRenderer.transform.parent, false);
            EditorCoroutineUtility.StartCoroutine(RecalculateSkinnedMeshBoundsCoroutine(newDestinationRenderer), newDestinationRenderer);
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Bake SkinnedMeshRenderer...", false)]
        static void BakeSkinnedMesh(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Select SkinnedMeshRender to bake.",
                    "Ok");
            }
            
            if (Selection.objects.Length != 1)
            {
                ErrorDialogue();
                return;
            }

            SkinnedMeshRenderer sourceRenderer = (Selection.objects[0] as GameObject).GetComponent<SkinnedMeshRenderer>();
            if (sourceRenderer == null)
            {
                ErrorDialogue();
                return;
            }
            
            Mesh bakedMesh = new Mesh();
            sourceRenderer.BakeMesh(bakedMesh);
            string oldMeshPath = AssetDatabase.GetAssetPath(sourceRenderer.sharedMesh);
            var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, sourceRenderer.sharedMesh.name, "BakedSkinnedMesh");
            AssetDatabase.CreateAsset(bakedMesh, encryptedMeshPath);
            AssetDatabase.SaveAssets();

            GameObject gameObject = new GameObject($"{sourceRenderer.sharedMesh.name}BakedSkinnedMesh");
            gameObject.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
            gameObject.transform.position = sourceRenderer.transform.position;
            gameObject.transform.rotation = sourceRenderer.transform.rotation;
            Undo.RegisterCreatedObjectUndo(gameObject, "Baked SkinnedMeshRenderer");
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Transfer Mesh Colors...", false)]
        static void TransferColors(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must Select Bake MeshFilter first, then SkinnedMeshRenderer.",
                    "Ok");
            }
            
            if (Selection.objects.Length != 2)
            {
                ErrorDialogue();
                return;
            }

            MeshFilter sourceFilter = (Selection.objects[0] as GameObject).GetComponent<MeshFilter>();
            SkinnedMeshRenderer destinationRenderer = (Selection.objects[1] as GameObject).GetComponent<SkinnedMeshRenderer>();
            if (sourceFilter == null || destinationRenderer == null)
            {
                ErrorDialogue();
                return;
            }
            
            Mesh newDestinationMesh = MonoBehaviour.Instantiate(destinationRenderer.sharedMesh);
            newDestinationMesh.SetColors(sourceFilter.sharedMesh.colors);
            string oldMeshPath = AssetDatabase.GetAssetPath(destinationRenderer.sharedMesh);
            var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, sourceFilter.sharedMesh.name, "TransferredVertexColors");
            AssetDatabase.CreateAsset(newDestinationMesh, encryptedMeshPath);
            AssetDatabase.SaveAssets();
            
            Undo.RecordObject(destinationRenderer, "Transfer Mesh Colors...");
            destinationRenderer.sharedMesh = newDestinationMesh;
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Recalculate SkinnedMeshRenderer Bounds...", false)]
        static void RecalculateSkinnedMeshBounds(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must Select SkinnedMeshRenderer.",
                    "Ok");
            }
            
            if (Selection.objects.Length != 1)
            {
                ErrorDialogue();
                return;
            }
            
            SkinnedMeshRenderer renderer = (Selection.objects[0] as GameObject).GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                ErrorDialogue();
                return;
            }
            
            EditorCoroutineUtility.StartCoroutine(RecalculateSkinnedMeshBoundsCoroutine(renderer), renderer);
        }

        static IEnumerator RecalculateSkinnedMeshBoundsCoroutine(SkinnedMeshRenderer renderer)
        {
            renderer.sharedMesh.RecalculateBounds();
            renderer.updateWhenOffscreen = true;
            yield return null;
            Bounds bounds = renderer.localBounds;
            renderer.updateWhenOffscreen = false;
            yield return null;
            renderer.localBounds = bounds;
        }

        static string GetModifiedMeshPath(string path,string meshName, string appendText)
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path)) + $"_{meshName}_{appendText}.asset";
        }
    }
}