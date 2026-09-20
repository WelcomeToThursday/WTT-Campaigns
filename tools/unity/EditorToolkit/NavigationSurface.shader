Shader "Hidden/WTT/Campaigns/NavigationSurface"
{
    Properties
    {
        _Color ("Surface", Color) = (0.2,0.65,1,0.3)
        _Diagnostic ("Diagnostic mode", Float) = 0
        _IssueFilter ("Issue filter", Float) = 0
        _Stale ("Stale samples", Float) = 0
        _FloorBand ("Visible floor band", Vector) = (-100000,100000,0,0)
        _ProjectGround ("Ground projection enabled", Float) = 0
        _ProjectionReach ("Ground projection height range", Float) = 2
        _ReceiverTolerance ("Surface contact tolerance", Float) = 0.06
        _MeshContactRange ("Mesh floor below, above, step and slope", Vector) = (0.64,0.16,0.38,0.6428)
        _TerrainReceiver ("Terrain receiver enabled", Float) = 0
        _TerrainPixelError ("Terrain LOD pixel error", Float) = 5
        _ReceiverDepthPrecision ("Depth precision compensation", Float) = 1
        _TerrainHeightmap ("Supporting terrain height", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #include "UnityCG.cginc"
        float4 _Color;
        float _Diagnostic, _IssueFilter, _Stale;
        float4 _FloorBand;
        float _ProjectionReach, _ProjectGround;
        struct appdata { float4 vertex : POSITION; float2 issue : TEXCOORD0; };
        struct v2f { float4 position : SV_POSITION; float3 world : TEXCOORD0; float issue : TEXCOORD1; };
        v2f vert(appdata input)
        {
            v2f output;
            output.position = UnityObjectToClipPos(input.vertex);
            output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
            output.issue = input.issue.x;
            return output;
        }
        float flag(float value, float bit) { return fmod(floor(value / bit), 2); }
        float4 shade(v2f input)
        {
            clip(input.world.y - _FloorBand.x);
            clip(_FloorBand.y - input.world.y);
            float4 color = _Color;
            if (_Diagnostic > 0.5)
            {
                float issue = floor(input.issue + 0.5);
                if (_Stale > 0.5 || flag(issue, 64) > 0.5)
                {
                    float stripe = step(0.5, frac((input.world.x + input.world.z) * 2));
                    return float4(0.7,0.75,0.8, lerp(0.08,0.35,stripe));
                }
                if (_IssueFilter > 0.5 && flag(issue, _IssueFilter) < 0.5) { clip(-1); return float4(0,0,0,0); }
                float selected = _IssueFilter > 0.5 ? _IssueFilter : issue;
                color = float4(0.2,0.75,1,0.18);
                if (flag(selected,4) > 0.5) color = float4(0.75,0.25,1,0.6);
                if (flag(selected,8) + flag(selected,16) + flag(selected,32) > 0.5) color = float4(1,0.65,0.08,0.55);
                if (flag(selected,1) + flag(selected,2) > 0.5) color = float4(1,0.12,0.12,0.6);
            }
            return color;
        }
        float4 visible(v2f input) : SV_Target { return shade(input); }
        ENDCG
        Pass
        {
            Name "RawSurface"
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment visible
            ENDCG
        }
        // Ground coverage is a depth decal, not a lifted triangle surface. The
        // GPU extrudes each existing triangle into a bounded vertical prism and
        // shades only the visible scene point inside that footprint.
        Pass
        {
            Name "GroundProjection"
            Cull Front ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma geometry projectGeometry
            #pragma fragment projectGround
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _TerrainHeightmap;
            float4 _TerrainRegion, _TerrainHeight;
            float4 _MeshContactRange;
            float _TerrainReceiver, _ReceiverTolerance, _TerrainPixelError, _ReceiverDepthPrecision;
            float sceneEyeDepth(float raw)
            {
                // Avoid subtracting large, almost-equal Z-buffer coefficients
                // when a small near plane is paired with a kilometre-scale view.
                #if !defined(UNITY_REVERSED_Z)
                    raw=1-raw;
                #endif
                float nearFar=_ProjectionParams.y/_ProjectionParams.z;
                return _ProjectionParams.y/(raw*(1-nearFar)+nearFar);
            }
            struct projected
            {
                float4 position : SV_POSITION;
                float4 screen : TEXCOORD0;
                float3 a : TEXCOORD1;
                float3 b : TEXCOORD2;
                float3 c : TEXCOORD3;
                float issue : TEXCOORD4;
                float3 ray : TEXCOORD5;
                float eye : TEXCOORD6;
            };
            void face(float3 p, float3 q, float3 r, float3 a, float3 b, float3 c, float issue, inout TriangleStream<projected> stream)
            {
                projected o;
                o.a = a; o.b = b; o.c = c; o.issue = issue;
                o.position = mul(UNITY_MATRIX_VP, float4(p,1)); o.screen = ComputeScreenPos(o.position); o.ray = p - _WorldSpaceCameraPos; o.eye = -mul(UNITY_MATRIX_V,float4(p,1)).z; stream.Append(o);
                o.position = mul(UNITY_MATRIX_VP, float4(q,1)); o.screen = ComputeScreenPos(o.position); o.ray = q - _WorldSpaceCameraPos; o.eye = -mul(UNITY_MATRIX_V,float4(q,1)).z; stream.Append(o);
                o.position = mul(UNITY_MATRIX_VP, float4(r,1)); o.screen = ComputeScreenPos(o.position); o.ray = r - _WorldSpaceCameraPos; o.eye = -mul(UNITY_MATRIX_V,float4(r,1)).z; stream.Append(o);
                stream.RestartStrip();
            }
            [maxvertexcount(24)]
            void projectGeometry(triangle v2f input[3], inout TriangleStream<projected> stream)
            {
                if (_ProjectGround < .5) return;
                float3 a=input[0].world, b=input[1].world, c=input[2].world;
                if (cross(b-a,c-a).y < 0) { float3 swap=b; b=c; c=swap; }
                float3 up=float3(0,_ProjectionReach,0);
                float3 at=a+up, bt=b+up, ct=c+up, ab=a-up, bb=b-up, cb=c-up;
                float issue=input[0].issue;
                face(at,bt,ct,a,b,c,issue,stream); face(cb,bb,ab,a,b,c,issue,stream);
                face(at,ab,bb,a,b,c,issue,stream); face(at,bb,bt,a,b,c,issue,stream);
                face(bt,bb,cb,a,b,c,issue,stream); face(bt,cb,ct,a,b,c,issue,stream);
                face(ct,cb,ab,a,b,c,issue,stream); face(ct,ab,at,a,b,c,issue,stream);
            }
            float4 projectGround(projected input) : SV_Target
            {
                float2 uv=input.screen.xy/input.screen.w;
                float depth=SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,uv);
                // No scene surface at the far plane: never paint the sky.
                clip(.99999 - Linear01Depth(depth));
                // Interpolate the camera ray from the prism's rendered back face.
                // Both it and scene depth belong to this render event; no camera
                // matrix reconstructed later by an image effect can move coverage.
                float eye=sceneEyeDepth(depth);
                // A native 24-bit depth sample represents an interval, which
                // grows at long range. Do not mistake that quantization for a
                // real separation between the terrain and its receiver.
                float depthStep=.5/16777215.0;
                float eyeLow=sceneEyeDepth(saturate(depth-depthStep));
                float eyeHigh=sceneEyeDepth(saturate(depth+depthStep));
                float eyeError=max(abs(eyeLow-eye),abs(eyeHigh-eye));
                float heightError=eyeError*abs(input.ray.y/input.eye)*_ReceiverDepthPrecision;
                float3 world=_WorldSpaceCameraPos + input.ray*(eye/input.eye);
                if (unity_OrthoParams.w > .5)
                    world=_WorldSpaceCameraPos + input.ray - UNITY_MATRIX_V[2].xyz*(eye-input.eye);
                float2 v0=input.b.xz-input.a.xz, v1=input.c.xz-input.a.xz, v2=world.xz-input.a.xz;
                float determinant=v0.x*v1.y-v1.x*v0.y;
                clip(abs(determinant)-.000001);
                float u=(v2.x*v1.y-v1.x*v2.y)/determinant;
                float v=(v0.x*v2.y-v2.x*v0.y)/determinant;
                clip(min(min(u,v),1-u-v)+.00001);
                float height=input.a.y+u*(input.b.y-input.a.y)+v*(input.c.y-input.a.y);
                clip(_ProjectionReach+heightError-abs(world.y-height));
                // Scene depth includes cutout grass, foliage and prop tops. A
                // walkable-looking normal alone does not make those receivers.
                // Terrain triangles use the actual height field. Mesh floors
                // also account for the voxel lift and stair smoothing of a bake.
                float receiverHeight=height;
                if (_TerrainReceiver > .5)
                {
                    float2 terrainUV=(world.xz-_TerrainRegion.xy)*_TerrainRegion.zw;
                    clip(min(min(terrainUV.x,terrainUV.y),min(1-terrainUV.x,1-terrainUV.y)));
                    terrainUV=terrainUV*_TerrainHeight.z+_TerrainHeight.w;
                    receiverHeight=_TerrainHeight.x+UnpackHeightmap(tex2Dlod(_TerrainHeightmap,float4(terrainUV,0,0)))*_TerrainHeight.y;
                }
                float tolerance=_ReceiverTolerance;
                float3 normal=normalize(cross(ddx(world),ddy(world)));
                if (_TerrainReceiver > .5)
                {
                    // Native terrain simplifies with distance, while its height
                    // texture retains full detail. Allow its screen-space LOD
                    // error at every distance. Bound the
                    // allowance so distant props cannot become ground receivers.
                    float pixelWorld=2*max(0,eye)/max(.0001,abs(UNITY_MATRIX_P[1][1])*_ScreenParams.y);
                    tolerance=max(tolerance,min(.35,pixelWorld*_TerrainPixelError));
                    if (abs(world.y-receiverHeight) > _ReceiverTolerance+heightError)
                    {
                        // Extra height allowance belongs to a simplified terrain
                        // surface, not to raised leaves. Require its orientation
                        // to agree with the terrain before using that allowance.
                        float2 terrainUV=(world.xz-_TerrainRegion.xy)*_TerrainRegion.zw*_TerrainHeight.z+_TerrainHeight.w;
                        float texel=2*_TerrainHeight.w;
                        float left=UnpackHeightmap(tex2Dlod(_TerrainHeightmap,float4(terrainUV-float2(texel,0),0,0)))*_TerrainHeight.y;
                        float right=UnpackHeightmap(tex2Dlod(_TerrainHeightmap,float4(terrainUV+float2(texel,0),0,0)))*_TerrainHeight.y;
                        float back=UnpackHeightmap(tex2Dlod(_TerrainHeightmap,float4(terrainUV-float2(0,texel),0,0)))*_TerrainHeight.y;
                        float front=UnpackHeightmap(tex2Dlod(_TerrainHeightmap,float4(terrainUV+float2(0,texel),0,0)))*_TerrainHeight.y;
                        float2 span=2*texel/(_TerrainRegion.zw*_TerrainHeight.z);
                        float3 terrainNormal=normalize(float3((left-right)/span.x,1,(back-front)/span.y));
                        float3 facingNormal=normal.y < 0 ? -normal : normal;
                        clip(dot(facingNormal,terrainNormal)-.9659);
                    }
                }
                if (_TerrainReceiver > .5)
                    clip(tolerance+heightError-abs(world.y-receiverHeight));
                else
                {
                    float3 navigationNormal=normalize(cross(input.b-input.a,input.c-input.a));
                    // A sloping navigation plane bridges individual step treads.
                    // Flat floors do not gain that extra upward reach: it would
                    // label furniture resting on the floor as walkable coverage.
                    float stepAllowance=abs(navigationNormal.y) < .9962 ? _MeshContactRange.z : 0;
                    float offset=world.y-height;
                    clip(_MeshContactRange.x+heightError+offset);
                    clip(_MeshContactRange.y+stepAllowance+heightError-offset);
                    clip(abs(normal.y)-_MeshContactRange.w);
                }
                // Reject walls and steep sides of props, rather than turning them
                // into apparent walkable coverage. Two-sided derivative normal.
                clip(abs(normal.y)-.45);
                v2f shading;
                shading.position=input.position; shading.world=world; shading.issue=input.issue;
                return shade(shading);
            }
            ENDCG
        }
    }
    Fallback Off
}
