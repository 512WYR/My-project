Shader "GameMake/UI/Rolling CRT Distortion"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]
        GrabPass { }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 grab:TEXCOORD1; float4 local:TEXCOORD2; };
            sampler2D _GrabTexture;
            float4 _GrabTexture_TexelSize;
            float4 _ClipRect;
            float4 _Bands[8];
            int _BandCount;
            v2f vert(appdata v)
            {
                v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.grab=ComputeGrabScreenPos(o.vertex); o.uv=v.uv; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float shift=0, lineAlpha=0;
                [unroll] for(int b=0;b<8;b++)
                {
                    if(b>=_BandCount) break;
                    float4 band=_Bands[b];
                    float d=(i.uv.y-band.x)/max(band.y*.5,.0001);
                    float envelope=1-smoothstep(.25,1,abs(d));
                    shift+=band.z*envelope*(.8+.2*cos(d*3.14159));
                    // The visible line and the shear share the exact same moving center and width.
                    // A defined luminous core makes the interference visible even over a dark screen.
                    float aa=max(fwidth(i.uv.y),.00001);
                    float core=saturate((band.y*.5-abs(i.uv.y-band.x))/aa+.5);
                    lineAlpha+=band.w*core;
                }
                float edge=smoothstep(0,.025,i.uv.x)*smoothstep(0,.025,1-i.uv.x)
                    *smoothstep(0,.012,i.uv.y)*smoothstep(0,.012,1-i.uv.y);
                float2 uv=i.grab.xy/i.grab.w;
                uv.x+=clamp(shift,-4,4)*edge*abs(_GrabTexture_TexelSize.x);
                fixed4 result=tex2D(_GrabTexture,uv);
                float ink=dot(result.rgb,float3(.2126,.7152,.0722));
                float preserveInk=lerp(1,.12,smoothstep(.18,.65,ink));
                result.rgb=lerp(result.rgb,float3(.63,.72,.65),min(lineAlpha,.22)*edge*preserveInk);
                result.a=1;
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
