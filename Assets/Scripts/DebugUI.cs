using UnityEngine;

namespace CollisionCheck
{
    [RequireComponent(typeof(CollisionManager))]
    public class CollisionDebugGUI : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool _showDebugOverlay = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F3;

        private CollisionManager _manager;

        private void Awake()
        {
            _manager = GetComponent<CollisionManager>();
        }

        private void Update()
        {
            // // if (Input.GetKeyDown(_toggleKey))
            // {
            //     _showDebugOverlay = !_showDebugOverlay;
            // }
        }

        private void OnGUI()
        {
            if (!_showDebugOverlay || _manager == null) return;

            // Draw semi-transparent background panel
            GUI.Box(new Rect(10, 10, 320, 180), "Collision Pipeline Debug");

            GUILayout.BeginArea(new Rect(20, 35, 300, 150));

            GUILayout.Label($"<b>Status:</b> {(_manager.IsPipelineActive ? "<color=green>Active</color>" : "<color=yellow>Waiting for Meshes</color>")}");
            GUILayout.Label($"<b>Probe Nodes:</b> {_manager.DebugProbeNodeCount:N0}");
            GUILayout.Label($"<b>Target Nodes:</b> {_manager.DebugTargetNodeCount:N0}");
            GUILayout.Space(5);
            
            GUILayout.Label($"<b>Evaluated Candidates:</b> <color=cyan>{_manager.DebugCandidateCount:N0}</color> / {_manager.MaxCandidatePairs:N0}");
            
            string clearanceText = _manager.DebugMinClearance == float.MaxValue 
                ? "N/A" 
                : $"{_manager.DebugMinClearance * 1000f:F2} mm ({_manager.DebugMinClearance:F4} m)";
                
            GUILayout.Label($"<b>Min Clearance:</b> {clearanceText}");

            GUILayout.Space(5);
            GUILayout.Label("<size=10><i>Press F3 to toggle this window</i></size>");

            GUILayout.EndArea();
        }
    }
}