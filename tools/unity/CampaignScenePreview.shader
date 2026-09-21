Shader "WTT/CampaignScenePreview"
{
    Properties { _MainTex ("Diffuse", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) _UseOpacity ("Use opacity", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float _UseOpacity;
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; };
            v2f vert(appdata_base v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); o.normal = UnityObjectToWorldNormal(v.normal); return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv) * _Color;
                clip(lerp(1, color.a, _UseOpacity) - .1);
                float key = saturate(dot(normalize(i.normal), normalize(float3(1, .8, -1))));
                float fill = saturate(dot(normalize(i.normal), normalize(float3(-1, .3, .5))));
                return fixed4(color.rgb * (.45 + .7 * key + .2 * fill), 1);
            }
            ENDCG
        }
    }
}
