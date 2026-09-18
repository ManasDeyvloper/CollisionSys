Shader "CollisionCheck/ProbeClearanceHighlight"
{
    // Put this on the probe's material (the one RuntimeMeshLoader's fallback material slot
    // points at, or swap it in afterward). ClearanceFeedbackController binds _TriangleClearance
    // and the color/threshold properties below via a MaterialPropertyBlock once the probe has
    // loaded -- see ClearanceFeedbackController.TrySetup().
    //
    // _BaseColor is also driven per-frame by ClearanceFeedbackController.ApplyClearance() as
    // the existing whole-probe "worst clearance anywhere" tint; this shader layers the
    // PER-TRIANGLE highlight from _TriangleClearance on top of that, so only the triangles
    // that actually have a nearby/colliding target triangle this frame light up individually,
    // while the rest of the probe still shows the overall gradient.
    //
    // Requires SV_PrimitiveID in the fragment stage (no geometry shader), which needs
    // shader model 4.5+ -- fine on D3D11/12, Vulkan, Metal; NOT supported on OpenGL ES /
    // WebGL, so this won't work as-is on those targets.
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _SafeColor("Safe Color", Color) = (0,1,0,1)
        _WarningColor("Warning Color", Color) = (1,1,0,1)
        _PenetratingColor("Penetrating Color", Color) = (1,0,0,1)
        _SafeClearance("Safe Clearance (m)", Float) = 0.005
        _WarningClearance("Warning Clearance (m)", Float) = 0.001
        _HighlightRange("Highlight Fade Range (m)", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SafeColor;
                float4 _WarningColor;
                float4 _PenetratingColor;
                float _SafeClearance;
                float _WarningClearance;
                float _HighlightRange;
            CBUFFER_END

            // Per-triangle signed distance for THIS probe, indexed by the mesh's own original
            // draw-order triangle index (matches SV_PrimitiveID). Written on the CPU side by
            // ProbeTriangleClearanceJob via ProbeBvh.TriangleClearance / UploadTriangleClearance().
            // float.MaxValue = "no candidate touched this triangle this frame".
            StructuredBuffer<float> _TriangleClearance;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN, uint primitiveID : SV_PrimitiveID) : SV_Target
            {
                float d = _TriangleClearance[primitiveID];

                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction)) * 0.5 + 0.5;

                // Untouched this frame -> just the base color (the whole-probe gradient from
                // ClearanceFeedbackController.ApplyClearance()), lit.
                if (d >= 3.0e38)
                    return float4(_BaseColor.rgb * ndotl, _BaseColor.a);

                float3 highlight;
                if (d < 0.0)
                {
                    highlight = _PenetratingColor.rgb;
                }
                else if (d >= _SafeClearance)
                {
                    highlight = _SafeColor.rgb;
                }
                else
                {
                    float t = saturate((d - _WarningClearance) / max(_SafeClearance - _WarningClearance, 1e-6));
                    highlight = lerp(_WarningColor.rgb, _SafeColor.rgb, t);
                }

                // Fade toward the base color as the triangle's own clearance gets further from
                // zero, so only genuinely tight/colliding triangles read as strongly colored
                // rather than every triangle the broad phase merely happened to pass through.
                float blend = d < 0.0 ? 1.0 : saturate(1.0 - d / max(_HighlightRange, 1e-6));
                float3 finalColor = lerp(_BaseColor.rgb, highlight, blend);

                return float4(finalColor * ndotl, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
