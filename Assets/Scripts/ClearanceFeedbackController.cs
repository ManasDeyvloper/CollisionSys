using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// Step 5 of the per-frame pipeline. Pure presentation -- takes the aggregated signed
    /// distance for this frame and drives a global color gradient on the probe's renderer via
    /// a MaterialPropertyBlock, AND keeps the per-triangle highlight buffer
    /// (ProbeClearanceHighlight.shader reads this) bound so specific colliding/close triangles
    /// light up individually. This is the ONLY script that knows about rendering; everything
    /// upstream deals purely in geometry.
    ///
    /// Meant to live ON the probe GameObject (alongside ProbeBvh), since that's whose renderer
    /// and mesh it's driving. Its renderer does not exist yet at scene load -- the probe's mesh
    /// (and the MeshRenderer RuntimeMeshLoader adds for it) only shows up once the user has
    /// picked and imported a glTF file -- so setup is deferred out of Awake() and instead
    /// happens once both ProbeBvh AND TargetBvh report they're initialized. (Strictly the
    /// renderer only depends on the probe; gating on the target too avoids showing "safe" green
    /// on a probe before there's even a target to compare it against.)
    /// </summary>
    public class ClearanceFeedbackController : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Usually a sibling component on this same GameObject.")]
        [SerializeField] private ProbeBvh _probe;
        [SerializeField] private TargetBvh _target;

        [Header("Thresholds (meters)")]
        [Tooltip("At or above this clearance, show full 'safe' color.")]
        public float SafeClearance = 0.005f; // 5mm, matches the source doc's example

        [Tooltip("Below this clearance (but still >= 0), show full 'warning' color.")]
        public float WarningClearance = 0.001f; // 1mm

        [Header("Colors")]
        public Color SafeColor = Color.green;
        public Color WarningColor = Color.yellow;
        public Color PenetratingColor = Color.red;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SafeColorId = Shader.PropertyToID("_SafeColor");
        private static readonly int WarningColorId = Shader.PropertyToID("_WarningColor");
        private static readonly int PenetratingColorId = Shader.PropertyToID("_PenetratingColor");
        private static readonly int SafeClearanceId = Shader.PropertyToID("_SafeClearance");
        private static readonly int WarningClearanceId = Shader.PropertyToID("_WarningClearance");
        private static readonly int TriangleClearanceBufferId = Shader.PropertyToID("_TriangleClearance");

        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;

        /// <summary>True once the renderer exists and the highlight buffer is bound. Guards
        /// ApplyClearance() so CollisionManager can call it unconditionally every frame without
        /// caring whether the probe mesh has actually loaded yet.</summary>
        public bool IsReady { get; private set; }

        private bool _probeReady;
        private bool _targetReady;

        private void Awake()
        {
            if (_probe == null) _probe = GetComponent<ProbeBvh>();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (_probe != null) _probe.Initialized += HandleProbeInitialized;
            if (_target != null) _target.Initialized += HandleTargetInitialized;

            // In case both were already loaded before this component enabled (e.g. re-enabling
            // after being toggled off).
            if (_probe != null && _probe.IsInitialized) HandleProbeInitialized();
            if (_target != null && _target.IsInitialized) HandleTargetInitialized();
        }

        private void OnDisable()
        {
            if (_probe != null) _probe.Initialized -= HandleProbeInitialized;
            if (_target != null) _target.Initialized -= HandleTargetInitialized;
        }

        private void HandleProbeInitialized()
        {
            _probeReady = true;
            TrySetup();
        }

        private void HandleTargetInitialized()
        {
            _targetReady = true;
            TrySetup();
        }

        private void TrySetup()
        {
            if (IsReady || !_probeReady || !_targetReady) return;

            // Only valid to fetch this now -- RuntimeMeshLoader adds the MeshRenderer as part
            // of combining the imported glTF hierarchy into one mesh, which happens before
            // ProbeBvh.Initialize() (and therefore its Initialized event) fires.
            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
            {
                Debug.LogWarning($"{nameof(ClearanceFeedbackController)} on '{name}' found no Renderer " +
                                  "after the probe finished loading -- is this component on the same " +
                                  "GameObject as ProbeBvh / the imported mesh?", this);
                return;
            }

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(SafeColorId, SafeColor);
            _propertyBlock.SetColor(WarningColorId, WarningColor);
            _propertyBlock.SetColor(PenetratingColorId, PenetratingColor);
            _propertyBlock.SetFloat(SafeClearanceId, SafeClearance);
            _propertyBlock.SetFloat(WarningClearanceId, WarningClearance);
            if (_probe.TriangleClearanceBuffer != null)
                _propertyBlock.SetBuffer(TriangleClearanceBufferId, _probe.TriangleClearanceBuffer);
            _renderer.SetPropertyBlock(_propertyBlock);

            IsReady = true;
        }

        /// <summary>Called once per frame by CollisionManager after the reduce job completes.
        /// No-ops until the probe/target have both loaded and setup has run.</summary>
        public void ApplyClearance(float signedDistance)
        {
            if (!IsReady) return;

            Color color;
            if (signedDistance < 0f)
            {
                color = PenetratingColor;
            }
            else if (signedDistance >= SafeClearance)
            {
                color = SafeColor;
            }
            else
            {
                float t = Mathf.InverseLerp(WarningClearance, SafeClearance, signedDistance);
                color = Color.Lerp(WarningColor, SafeColor, Mathf.Clamp01(t));
            }

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}