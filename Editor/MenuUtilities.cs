using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace GeoTetra.GTAvaUtil
{
    public class MenuUtilites
    {
        const string okText = "Ok";
        
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
        
        [MenuItem("Tools/GeoTetra/GTAvaUtil/Average Vertex Colors On MeshFilter...", false)]
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
            
            EditorCoroutineUtility.StartCoroutine(AverageVertexColorsOnMeshesCoroutine(filters), filters[0]);
        }

        static IEnumerator AverageVertexColorsOnMeshesCoroutine(List<MeshFilter> filters)
        {
            foreach (var meshFilter in filters)
            {
                EditorUtility.DisplayProgressBar("Averaging Vertex Colors..", "", 0);
            
                VertexColorSmoother smoother = new VertexColorSmoother(meshFilter);
                yield return smoother.RunCoroutine();
                var newColors = smoother.OutputColors;
                
                meshFilter.sharedMesh.colors = newColors;
                meshFilter.sharedMesh.UploadMeshData(false);

                smoother.Dispose();
                
                Debug.Log($"Mesh colors averaged on {meshFilter}! This does not save the mesh. Right now I use this before using TransferColors onto the mesh that does save. Might change this in the future.");
                
                EditorUtility.ClearProgressBar();
            }
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
