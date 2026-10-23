// Translucent silhouette with a glowing rim (URP, unlit): summoned copies such as Zed's shadow clones.
Shader "AutoChess/Shadow Clone" {
    Properties {
        _Color ("Body", Color) = (0.08, 0.04, 0.14, 0.75)
        [HDR] _RimColor ("Rim", Color) = (0.7, 0.35, 1.4, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
    }
    SubShader {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass {
            Name "Unlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings vert(Attributes input) {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half rim = pow(1 - facing, _RimPower);
                half3 color = _Color.rgb + _RimColor.rgb * rim;
                half alpha = saturate(_Color.a + rim * _RimColor.a * 0.5);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
