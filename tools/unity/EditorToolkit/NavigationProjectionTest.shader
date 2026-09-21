// Validation fixture only; not included in the runtime bundle.
Shader "Hidden/WTT/Campaigns/NavigationProjectionTest"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert addshadow
        struct Input { float3 worldPos; };
        void surf(Input IN, inout SurfaceOutput o) { o.Albedo=float3(.1,.1,.1); o.Alpha=1; }
        ENDCG
    }
    Fallback Off
}
