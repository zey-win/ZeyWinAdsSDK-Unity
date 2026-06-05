Shader "Hidden/ZeyWinAds/PerceivedSmoothness"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "white" {}
        _HistoryTex ("Previous Frame", 2D) = "black" {}
        _Blend ("Blend", Range(0, 0.35)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HistoryTex;
            float _Blend;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 current = tex2D(_MainTex, i.uv);
                fixed4 history = tex2D(_HistoryTex, i.uv);
                fixed4 blended = lerp(current, history, saturate(_Blend));
                blended.rgb = lerp(blended.rgb, current.rgb, 0.72);
                blended.a = current.a;
                return blended;
            }
            ENDCG
        }
    }
}
