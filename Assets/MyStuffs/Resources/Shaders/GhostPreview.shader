Shader "Custom/GhostPreview"
{
    Properties
    {
        [HDR] _GhostColor ("Ghost Color", Color) = (1, 1, 0, 1) // Default Yellow/Gold
        _RimPower ("Rim Power", Range(0.1, 10.0)) = 4.0
        _BaseOpacity ("Base Opacity", Range(0.0, 1.0)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // PASS 1: Depth Pre-pass
        // This solves the "inside-out" look by writing to the Z-buffer first
        // without drawing any pixels.
        Pass
        {
            Name "DepthPrepass"
            ColorMask 0
            ZWrite On
        }

        // PASS 2: Glowing Rim Light
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _GhostColor;
                float _RimPower;
                float _BaseOpacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.normalWS);
                float3 viewDir = normalize(IN.viewDirWS);
                
                // Fresnel/Rim calculation
                float NdotV = saturate(dot(normal, viewDir));
                float rim = 1.0 - NdotV;
                rim = pow(rim, _RimPower);

                // Add rim intensity to base opacity, clamped by the color's alpha
                float alpha = saturate(rim + _BaseOpacity) * _GhostColor.a;
                
                return half4(_GhostColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
