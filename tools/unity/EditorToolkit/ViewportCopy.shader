Shader "Hidden/WTT/Campaigns/ViewportCopy"
{
    Properties
    {
        _MainTex ("Native frame", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 frag(v2f_img input) : SV_Target
            {
                // The native Direct3D frame and Toolkit Image use opposite Y origins.
                // Correct only presentation; scene rays and overlays stay camera-oriented.
                float2 uv = input.uv;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
                // Native post-processing alpha is effect data, not UI opacity.
                float3 color = tex2D(_MainTex, uv).rgb;
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
