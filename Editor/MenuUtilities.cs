using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace GeoTetra.GTAvaUtil
{
    public class MenuUtilites
    {
        const string okText = "Ok";
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Transfer SkinnedMeshRenderer Bones To Another SkinnedMeshRenderer...", false)]
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

        [MenuItem("Tools/GeoTetra/GTAvaUtil/Bake SkinnedMeshRenderer To MeshRenderer...", false)]
        static void BakeSkinnedMesh(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Select SkinnedMeshRenders to bake.",
                    "Ok");
            }

            if (Selection.objects.Length == 0)
            {
                ErrorDialogue();
                return;
            }

            int processedCount = 0;
            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    SkinnedMeshRenderer sourceRenderer = selectedGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (sourceRenderer == null)
                    {
                        continue;
                    }

                    Mesh bakedMesh = new Mesh();
                    sourceRenderer.BakeMesh(bakedMesh);
                    string oldMeshPath = AssetDatabase.GetAssetPath(sourceRenderer.sharedMesh);
                    var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, sourceRenderer.sharedMesh.name, "BakedSkinnedMesh");
                    AssetDatabase.CreateAsset(bakedMesh, encryptedMeshPath);
                    AssetDatabase.SaveAssets();

                    GameObject gameObject = new GameObject(Path.GetFileNameWithoutExtension(encryptedMeshPath));
                    gameObject.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
                    gameObject.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                    gameObject.transform.position = sourceRenderer.transform.position;
                    gameObject.transform.rotation = sourceRenderer.transform.rotation;
                    Undo.RegisterCreatedObjectUndo(gameObject, "Baked SkinnedMeshRenderer");

                    processedCount++;
                }
            }

            if (processedCount == 0)
            {
                ErrorDialogue();
            }
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
            var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, sourceFilter.sharedMesh.name,"TransferredVertexColors");
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
                    "Must Select SkinnedMeshRenderers.",
                    "Ok");
            }
            
            if (Selection.objects.Length == 0)
            {
                ErrorDialogue();
                return;
            }
            
            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    SkinnedMeshRenderer renderer = selectedGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (renderer != null)
                    {
                        EditorCoroutineUtility.StartCoroutine(RecalculateSkinnedMeshBoundsCoroutine(renderer), renderer);
                    }
                }
            }
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Add Probe Anchor From Averaged Mesh Positions...", false)]
        static void AddProbeAtAveragedMeshPositions(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must select multiple SkinnedMeshRenderer's.",
                    "Ok");
            }
            
            if (Selection.objects.Length < 2)
            {
                ErrorDialogue();
                return;
            }

            List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
            List<MeshRenderer> meshRenderers = new List<MeshRenderer>();
            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    SkinnedMeshRenderer skinnedMeshRenderer = selectedGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (skinnedMeshRenderer != null)
                    {
                        skinnedMeshRenderers.Add(skinnedMeshRenderer);
                        continue;
                    }
                    
                    MeshRenderer renderer = selectedGameObject.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        meshRenderers.Add(renderer);
                    }
                }
            }

            if (skinnedMeshRenderers.Count + meshRenderers.Count < 2)
            {
                ErrorDialogue();
                return;
            }

            Renderer firstRender;
            if (skinnedMeshRenderers.Count > 0)
                firstRender = skinnedMeshRenderers[0];
            else
                firstRender = meshRenderers[0];
            
            Bounds worldBounds = new Bounds(firstRender.transform.position, Vector3.zero);
            foreach (var renderer in skinnedMeshRenderers)
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
            foreach (var renderer in meshRenderers)
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
            
            GameObject anchorGameObject = new GameObject("ProbeAnchor");
            anchorGameObject.transform.SetParent(firstRender.transform.root);
            anchorGameObject.transform.position = worldBounds.center;
            
            foreach (var renderer in skinnedMeshRenderers)
            {
                renderer.probeAnchor = anchorGameObject.transform;
            }
            foreach (var renderer in meshRenderers)
            {
                renderer.probeAnchor = anchorGameObject.transform;
            }
        }

        class BakeApplyMesh
        {
            public GameObject GameObject;
            public MeshFilter MeshFilter;
            public SkinnedMeshRenderer SkinnedMeshRenderer;
            public Mesh SharedMesh;
            public readonly List<SubMeshDescriptor> SubMeshDescriptors = new List<SubMeshDescriptor>();
            public readonly List<Material> BakeMaterials = new List<Material>();
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Bake Vertex AO On Combined MeshRenders+MeshFilters and Apply to Vertex Color...", false)]
        static void BakeVertexAOOnCombinedMeshes(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must Select GameObject with MeshFilter or SkinnedMeshRenderer.",
                    "Ok");
            }
            
            if (Selection.objects.Length == 0)
            {
                ErrorDialogue();
                return;
            }
            
            var combine = new List<CombineInstance>();
            var bakeApplyMeshes = new List<BakeApplyMesh>();
            var materials = new List<Material>();

            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    Mesh mesh = null;
                    Transform transform = null;
                    Matrix4x4 trs = Matrix4x4.identity;

                    MeshFilter selectedFilter = selectedGameObject.GetComponent<MeshFilter>();
                    SkinnedMeshRenderer selectedSkinnedMeshRenderer = selectedGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (selectedSkinnedMeshRenderer != null)
                    {
                        mesh = new Mesh();
                        selectedSkinnedMeshRenderer.BakeMesh(mesh);
                        selectedSkinnedMeshRenderer.gameObject.SetActive(false);
                        // for some reason you want scale to be one on skinned mesh renderers
                        trs = Matrix4x4.TRS(selectedSkinnedMeshRenderer.transform.position, selectedSkinnedMeshRenderer.transform.rotation, Vector3.one);
                    }
                    else if (selectedFilter != null)
                    {
                        mesh = selectedFilter.sharedMesh;
                        selectedFilter.gameObject.SetActive(false);
                        trs = Matrix4x4.TRS(selectedFilter.transform.position, selectedFilter.transform.rotation, selectedFilter.transform.lossyScale);
                    }
                    else
                    {
                        Debug.LogWarning($"No MeshFilter nor SkinnedMeshRender on selected {selectedGameObject.name}");
                    }

                    if (mesh == null)
                    {
                        continue;
                    }

                    var bakeApplyMesh = new BakeApplyMesh()
                    {
                        GameObject = selectedGameObject,
                        MeshFilter = selectedFilter,
                        SkinnedMeshRenderer = selectedSkinnedMeshRenderer,
                        SharedMesh = mesh
                    };
                    
                    for (int i = 0; i < mesh.subMeshCount; ++i)
                    {
                        Material material = new Material(Shader.Find("GeoTetra/GTAvaUtil/DebugVertexColor"))
                        {
                            name = selectedObject.name + i
                        };
                        materials.Add(material);
                        
                        combine.Add(new CombineInstance
                        {
                            mesh = mesh,
                            transform = trs,
                            subMeshIndex = i,
                        });
                        
                        var descriptor = mesh.GetSubMesh(i);
                        bakeApplyMesh.SubMeshDescriptors.Add(descriptor);
                        bakeApplyMesh.BakeMaterials.Add(material);
                    }
                    
                    bakeApplyMeshes.Add(bakeApplyMesh);
                }
            }

            if (combine.Count == 0)
            {
                ErrorDialogue();
                return;
            }

            GameObject gameObject = new GameObject("BakedAOCombinedMesh (Can Delete, only for debug)");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh();
            filter.sharedMesh.CombineMeshes(combine.ToArray(), false, true, false);
            
            EditorCoroutineUtility.StartCoroutine(BakeVertexAOOnCombinedMeshesCoroutine(filter, renderer, bakeApplyMeshes), filter);
        }
        
        static IEnumerator BakeVertexAOOnCombinedMeshesCoroutine(MeshFilter bakedMeshFilter, MeshRenderer renderer,  List<BakeApplyMesh> bakeApplyMeshes)
        {
            var baker = new BruteAOBaker(bakedMeshFilter);
            yield return baker.RunCoroutine();
            baker.Dispose();
            
            yield return null;
            
            VertexColorSmoother smoother = new VertexColorSmoother(bakedMeshFilter.sharedMesh, 2);
            yield return smoother.RunCoroutine();
            smoother.Dispose();
                
            yield return null;
            
            int submeshIndex = 0;
            foreach (var bakeApplyMesh in bakeApplyMeshes)
            {
                List<Color> combinedColors = new List<Color>();
                
                for (int i = 0; i < bakeApplyMesh.SubMeshDescriptors.Count; ++i)
                {
                    var submesh = bakedMeshFilter.sharedMesh.GetSubMesh(submeshIndex);
                    var newColors = new Color[submesh.vertexCount];
                    Array.Copy(bakedMeshFilter.sharedMesh.colors, submesh.firstVertex, newColors, 0, submesh.vertexCount);
                    combinedColors.AddRange(newColors);
                    submeshIndex++;
                }

                Mesh sourceMesh = null;
                if (bakeApplyMesh.SkinnedMeshRenderer != null)
                {
                    bakeApplyMesh.SkinnedMeshRenderer.gameObject.SetActive(true);
                    sourceMesh = bakeApplyMesh.SkinnedMeshRenderer.sharedMesh;
                }
                else if (bakeApplyMesh.MeshFilter != null)
                {
                    bakeApplyMesh.MeshFilter.gameObject.SetActive(true);
                    sourceMesh = bakeApplyMesh.MeshFilter.sharedMesh;
                }
                else
                    continue;

                Mesh newDestinationMesh = Object.Instantiate(sourceMesh);
                newDestinationMesh.SetColors(combinedColors.ToArray());
                
                try
                {
                    var oldMeshPath = AssetDatabase.GetAssetPath(sourceMesh);
                    var encryptedMeshPath = GetModifiedMeshPath(oldMeshPath, sourceMesh.name, "BakedVertexAO");
                    AssetDatabase.CreateAsset(newDestinationMesh, encryptedMeshPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Couldn't save new mesh for {sourceMesh.name}: {e.Message}");
                    continue;
                }

                if (bakeApplyMesh.SkinnedMeshRenderer != null)
                {
                    Undo.RecordObject(bakeApplyMesh.SkinnedMeshRenderer, "Bake Vertex AO...");
                    bakeApplyMesh.SkinnedMeshRenderer.sharedMesh = newDestinationMesh;
                }
                else if (bakeApplyMesh.MeshFilter != null)
                {
                    Undo.RecordObject(bakeApplyMesh.MeshFilter, "Bake Vertex AO...");
                    bakeApplyMesh.MeshFilter.sharedMesh = newDestinationMesh;
                }
            }
            
            Object.DestroyImmediate(bakedMeshFilter.gameObject);
            
            AssetDatabase.SaveAssets();
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Bake Vertex AO On MeshFilters...", false)]
        static void BakeVertexAOOnMeshes(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must Select GameObject with MeshFilter.",
                    "Ok");
            }
            
            if (Selection.objects.Length == 0)
            {
                ErrorDialogue();
                return;
            }

            List<MeshFilter> filters = new List<MeshFilter>();
            
            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    MeshFilter filter = selectedGameObject.GetComponent<MeshFilter>();
                    if (filter == null)
                    {
                        continue;
                    }
                    
                    filters.Add(filter);
                }
            }

            if (filters.Count == 0)
            {
                ErrorDialogue();
            }
            
            EditorCoroutineUtility.StartCoroutine(BakeVertexAOOnMeshesCoroutine(filters), filters[0]);
        }
        
        static IEnumerator BakeVertexAOOnMeshesCoroutine(List<MeshFilter> filters)
        {
            foreach (var meshFilter in filters)
            {
                foreach (var material in meshFilter.GetComponent<MeshRenderer>().materials)
                {
                    material.shader = Shader.Find("GeoTetra/GTAvaUtil/DebugVertexColor");
                }
                
                var baker = new BruteAOBaker(meshFilter);
                yield return baker.RunCoroutine();
                baker.Dispose();
            }
        }
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Average Vertex Colors On SkinnedMeshRenders or MeshFilters...", false)]
        static void AverageVertexColorsOnMeshes(MenuCommand command)
        {
            void ErrorDialogue()
            {
                EditorUtility.DisplayDialog("Insufficient Selection!",
                    "Must Select GameObject with MeshFilter.",
                    "Ok");
            }
            
            if (Selection.objects.Length == 0)
            {
                ErrorDialogue();
                return;
            }

            var gameObjects = new List<GameObject>();
            
            foreach (var selectedObject in Selection.objects)
            {
                if (selectedObject is GameObject selectedGameObject)
                {
                    MeshFilter filter = selectedGameObject.GetComponent<MeshFilter>();
                    SkinnedMeshRenderer skinnedMeshRenderer = selectedGameObject.GetComponent<SkinnedMeshRenderer>();
                    if (filter == null && skinnedMeshRenderer == null)
                    {
                        continue;
                    }
                    
                    gameObjects.Add(selectedGameObject);
                }
            }

            if (gameObjects.Count == 0)
            {
                ErrorDialogue();
            }
            
            EditorCoroutineUtility.StartCoroutine(AverageVertexColorsOnMeshesCoroutine(gameObjects), gameObjects[0]);
        }
        
        static IEnumerator AverageVertexColorsOnMeshesCoroutine(List<GameObject> gameObjects)
        {
            foreach (var gameObject in gameObjects)
            {
                var meshFilter = gameObject.GetComponent<MeshFilter>();
                var skinnedMeshRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
                Mesh sourceMesh = null;

                if (meshFilter != null)
                {
                    sourceMesh = meshFilter.sharedMesh;
                }
                else if (skinnedMeshRenderer != null)
                {
                    sourceMesh = skinnedMeshRenderer.sharedMesh;
                }
                else
                {
                    Debug.LogWarning($"{gameObject} did not have necessary renderer or mesh.");
                    continue;
                }
                
                Mesh newDestinationMesh = null;
                newDestinationMesh = Object.Instantiate(sourceMesh);
                
                VertexColorSmoother smoother = new VertexColorSmoother(newDestinationMesh, 1);
                yield return smoother.RunCoroutine();
                smoother.Dispose();

                try
                {
                    var sourceMeshPath = AssetDatabase.GetAssetPath(sourceMesh);
                    var destinationMeshpath = GetModifiedMeshPath(sourceMeshPath, sourceMesh.name, "AveragedVertices");
                    AssetDatabase.CreateAsset(newDestinationMesh, destinationMeshpath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Couldn't save new mesh for {sourceMesh.name}: {e.Message}");
                    continue;
                }
                
                if (skinnedMeshRenderer != null)
                {
                    Undo.RecordObject(skinnedMeshRenderer, "Average Vertex Colors...");
                    skinnedMeshRenderer.sharedMesh = newDestinationMesh;
                }
                else if (meshFilter != null)
                {
                    Undo.RecordObject(meshFilter, "Average Vertex Colors...");
                    meshFilter.sharedMesh = newDestinationMesh;
                }
            }
            
            AssetDatabase.SaveAssets();
        }
            
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Check for Update...", false)]
        static void CheckForUpdate()
        {
            var list = UnityEditor.PackageManager.Client.List();
            while (!list.IsCompleted)
            { }
            PackageInfo package = list.Result.FirstOrDefault(q => q.name == "com.geotetra.gtavautil");
            if (package == null)
            {
                EditorUtility.DisplayDialog("Not installed via UPM!",
                    "This upgrade option only works if you installed via UPM. Go to AvaCrypt github and reinstall via UPM if you wish to use this",
                    okText);
            }

            UnityEditor.PackageManager.Client.Add("https://github.com/rygo6/GTAvaUtil.git");
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
        
        static string GetModifiedMeshPath(string path, string meshName, string appendText)
        {
            // this little just of splits and _'s is to keep it from continually appending text to the name
            string filename = Path.GetFileNameWithoutExtension(path);
            string[] splitFileName = filename.Split('_');
            string[] splitMeshName = meshName.Split('_');
            string finalMeshName = splitMeshName.Length == 1 ? splitMeshName[0] : splitMeshName[1];
            return $"{Path.Combine(Path.GetDirectoryName(path), splitFileName[0])}_{finalMeshName}_{appendText}.asset";
        }
    }
}
