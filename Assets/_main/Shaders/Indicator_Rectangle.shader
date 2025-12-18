Shader "Custom/Indicator_Rectangle"
{
    Properties
    {
        _Color ("Color", Color) = (1,0,0,0.5)
        _TotalLength ("Total Length", Float) = 6
        _FadeLength ("Fade Length", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        LOD 100

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

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

            fixed4 _Color;
            float _TotalLength;
            float _FadeLength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Đảo chiều UV y để vùng mờ bắt đầu từ đáy (gần nhân vật)
                float invY = 1.0 - i.uv.y;

                // Khoảng cách dọc theo chiều dài hình chữ nhật
                float dist = invY * _TotalLength;

                float alpha;
                if (dist < _FadeLength)
                {
                    // 0 -> fadeLength trong suốt
                    alpha = 0;
                }
                else
                {
                    // Từ fadeLength đến totalLength tăng alpha tuyến tính từ 0 đến 1
                    alpha = saturate((dist - _FadeLength) / (_TotalLength - _FadeLength));
                }

                fixed4 col = _Color;
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}