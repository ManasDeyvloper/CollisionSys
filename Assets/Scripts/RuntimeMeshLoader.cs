using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using SimpleFileBrowser;
using UnityEngine;
using UnityEngine.Rendering;

namespace CollisionCheck
{
    /// <summary>
    /// Lets the user pick a glTF (.gltf/.glb) file for the probe and/or target via a file
    /// browser dialog, imports it at runtime, and hands the result to ProbeBvh/TargetBvh's
    /// Initialize(Mesh) -- which is why those two scripts no longer build in Awake().
    ///
    /// Assumes:
    /// - "SimpleFileBrowser" (yasirkula) for the native-feeling file picker. Any other file
    ///   browser asset works the same way -- swap the two FileBrowser calls in
    ///   LoadModelRoutine() for your picker's equivalent "show dialog, get a path" API.
    /// - "com.unity.cloud.gltfast" for runtime glTF import. If you're on UnityGLTF instead,
    ///   swap ImportGltfAsync()'s body for UnityGLTF's GltfImporter -- everything else
    ///   (file picking, mesh combining, handing off to ProbeBvh/TargetBvh) stays the same.
    ///
    /// A glTF file can contain many nodes/primitives; ProbeBvh/TargetBvh and the BVH builder
    /// expect a single triangle soup, so after import this combines every MeshFilter under
    /// the imported hierarchy into one Mesh (in the import root's local space) before handing
    /// it off.
    /// </summary>
    public class RuntimeMeshLoader : MonoBehaviour
    {
        [SerializeField] private ProbeBvh _probe;
        [SerializeField] private TargetBvh _target;

        [Tooltip("Empty transform the imported probe model is parented under. Should be the " +
                 "same GameObject ProbeBvh sits on (or a parent of it).")]
        [SerializeField] private Transform _probeRoot;

        [Tooltip("Empty transform the imported target model is parented under, positioned " +
                 "wherever the target belongs in the scene BEFORE loading -- TargetBvh bakes " +
                 "this transform in at Initialize() time.")]
        [SerializeField] private Transform _targetRoot;

        [Tooltip("Material applied to the combined mesh's renderer. Assign something simple " +
                 "(e.g. URP/Lit) -- ClearanceFeedbackController overrides its color at runtime " +
                 "via a MaterialPropertyBlock, so the base material's own color doesn't matter.")]
        [SerializeField] private Material _fallbackMaterial;

        public event Action ProbeLoaded;
        public event Action TargetLoaded;

        private void Awake()
        {
            FileBrowser.SetFilters(true, new FileBrowser.Filter("glTF", ".glb", ".gltf"));
            FileBrowser.SetDefaultFilter(".glb");
        }

        /// <summary>Wire this to a UI button for picking the probe (handheld tool/gear) model.</summary>
        [ContextMenu("Load Probe")]
        public void LoadProbeModel() => StartCoroutine(LoadModelRoutine(_probeRoot, OnProbeMeshReady));

        /// <summary>Wire this to a UI button for picking the target (engine bay/housing) model.</summary>
        [ContextMenu("Load Target")]
        public void LoadTargetModel() => StartCoroutine(LoadModelRoutine(_targetRoot, OnTargetMeshReady));

        private void OnProbeMeshReady(Mesh mesh)
        {
            _probe.Initialize(mesh);
            ProbeLoaded?.Invoke();
        }

        private void OnTargetMeshReady(Mesh mesh)
        {
            _target.Initialize(mesh);
            TargetLoaded?.Invoke();
        }

        private IEnumerator LoadModelRoutine(Transform intoRoot, Action<Mesh> onMeshReady)
        {
            yield return FileBrowser.WaitForLoadDialog(
                FileBrowser.PickMode.Files,
                allowMultiSelection: false,
                initialPath: null,
                initialFilename: null,
                title: "Select glTF model",
                loadButtonText: "Load");

            if (!FileBrowser.Success) yield break;

            string path = FileBrowser.Result[0];

            Task<Mesh> importTask = ImportGltfAsync(path, intoRoot);
            while (!importTask.IsCompleted) yield return null;

            if (importTask.IsFaulted)
            {
                Debug.LogError($"glTF import threw for '{path}': {importTask.Exception}");
                yield break;
            }

            if (importTask.Result == null)
            {
                Debug.LogError($"glTF import returned no mesh for '{path}' -- check the console above for glTFast errors.");
                yield break;
            }

            onMeshReady(importTask.Result);
        }

        private async Task<Mesh> ImportGltfAsync(string path, Transform intoRoot)
        {
            // Clear anything previously loaded under this root (e.g. the user picked a
            // different file the second time).
            for (int i = intoRoot.childCount - 1; i >= 0; i--)
                Destroy(intoRoot.GetChild(i).gameObject);

            var gltf = new GltfImport();

            bool loaded = await gltf.Load(new Uri(path).AbsoluteUri);
            if (!loaded) return null;

            var instantiator = new GameObjectInstantiator(gltf, intoRoot);
            bool instantiated = await gltf.InstantiateMainSceneAsync(instantiator);
            if (!instantiated) return null;

            return CombineIntoSingleMesh(intoRoot);
        }

        /// <summary>
        /// Flattens every MeshFilter under the imported hierarchy into one mesh, expressed
        /// in intoRoot's local space, and puts that combined mesh on intoRoot itself (so
        /// ProbeBvh/TargetBvh -- which sit on intoRoot -- have a MeshFilter to reference if
        /// you want one for rendering, though BVH building itself no longer reads it off a
        /// component; the Mesh is passed directly).
        /// </summary>
        private Mesh CombineIntoSingleMesh(Transform intoRoot)
        {
            var filters = intoRoot.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0)
            {
                Debug.LogWarning($"No MeshFilters found under '{intoRoot.name}' after glTF import.");
                return null;
            }

            var combines = new List<CombineInstance>(filters.Length);
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    // Bake each node's world transform, then re-express relative to intoRoot,
                    // so the combined mesh ends up in intoRoot's LOCAL space regardless of how
                    // deep the glTF's node hierarchy was.
                    transform = intoRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix
                });
            }

            var combined = new Mesh { indexFormat = IndexFormat.UInt32 };
            combined.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
            combined.RecalculateBounds();

            // Give intoRoot its own renderer showing the combined mesh, and strip the
            // per-node renderers glTFast created -- otherwise everything would draw twice.
            var rootFilter = intoRoot.GetComponent<MeshFilter>();
            if (rootFilter == null) rootFilter = intoRoot.gameObject.AddComponent<MeshFilter>();
            rootFilter.sharedMesh = combined;

            var rootRenderer = intoRoot.GetComponent<MeshRenderer>();
            if (rootRenderer == null) rootRenderer = intoRoot.gameObject.AddComponent<MeshRenderer>();
            if (rootRenderer.sharedMaterial == null) rootRenderer.sharedMaterial = _fallbackMaterial;

            foreach (MeshFilter filter in filters)
            {
                if (filter.transform == intoRoot) continue;
                Destroy(filter.gameObject);
            }

            return combined;
        }
    }
}
