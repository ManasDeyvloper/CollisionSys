using UnityEngine;

namespace CollisionCheck
{
    /// <summary>
    /// Step 5 of the per-frame pipeline. Pure presentation -- takes the single aggregated
    /// signed distance for this frame and drives a color gradient on the probe's renderer via
    /// a MaterialPropertyBlock (avoids allocating a material instance per probe). This is the
    /// ONLY script that knows about rendering; everything upstream deals purely in geometry.
    /// </summary>
    // [RequireComponent(typeof(Renderer))]
    public class ClearanceFeedbackController : MonoBehaviour
    {
        [Header("Thresholds (meters)")]
        [Tooltip("At or above this clearance, show full 'safe' color.")]
        public float SafeClearance = 0.005f; // 5mm, matches the source doc's example

        [Tooltip("Below this clearance (but still >= 0), show full 'warning' color.")]
        public float WarningClearance = 0.001f; // 1mm

        [Header("Colors")]
        public Color SafeColor = Color.green;
        public Color WarningColor = Color.yellow;
        public Color PenetratingColor = Color.red;

        private static readonly int ColorPropertyId = Shader.PropertyToID("_BaseColor");

        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
        }

        /// <summary>Called once per frame by CollisionManager after the reduce job completes.</summary>
        public void ApplyClearance(float signedDistance)
        {
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
            _propertyBlock.SetColor(ColorPropertyId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
