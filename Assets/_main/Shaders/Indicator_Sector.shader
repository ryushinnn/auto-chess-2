Shader "Custom/Indicator_Sector"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0, 0, 0.5)
        _Angle ("Angle", Range(0,360)) = 90
        _FadeLength ("Fade Length", Float) = 1
        _TotalLength ("Total Length", Float) = 6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float _Angle;
            float _FadeLength;
            float _TotalLength;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 center = float2(0.5, 0.5);
                float2 delta = i.uv - center;

                // Mapping từ UV space về world unit (giả định UV 0.5 = 0)
                float dist = length(delta) * _TotalLength * 2;

                // Bỏ ngoài bán kính
                if (dist > _TotalLength)
                    discard;

                // Tính góc
                float angle = degrees(atan2(delta.y, delta.x));
                if (angle < 0) angle += 360.0;
                float halfAngle = _Angle * 0.5;
                if (angle > halfAngle && angle < (360 - halfAngle))
                    discard;

                // Phần fade
                if (dist < _FadeLength)
                    discard;

                float alpha = saturate((dist - _FadeLength) / (_TotalLength - _FadeLength));

                fixed4 col = _Color;
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}