// Rounded-rect UI shader.
//
// Used by the Beaches list cards (see Assets/Prefabs/UI/ListItem.prefab) so
// the photo thumbnails and gradient overlay get smooth, anti-aliased corners
// without a stencil Mask (whose binary alpha test produces jagged stair-step
// edges along curves).
//
// Requires a RectMask2D somewhere up the parent chain — the SDF rounds
// corners of the RectMask2D's _ClipRect, NOT the Image's own mesh rect.
// That decoupling lets the photo Image overflow past the card (AspectCover
// EnvelopeParent) while still producing rounded corners that align with the
// card's edges. Without a RectMask2D parent, no clipping is applied and the
// shader behaves like UI/Default.
Shader "UI/RoundedRect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Radius ("Corner Radius (canvas units)", Float) = 32

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4    _Color;
            fixed4    _TextureSampleAdd;
            float4    _ClipRect;
            float4    _MainTex_ST;
            float     _Radius;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                // SDF rounded-rect clip against the parent RectMask2D's
                // _ClipRect (canvas-space, not screen-space). Computing
                // distance in canvas units means _Radius is in canvas units
                // too, which is what we want for layout consistency. The
                // SDF replaces the binary UnityGet2DClipping call.
                float2 rectMin    = _ClipRect.xy;
                float2 rectMax    = _ClipRect.zw;
                float2 rectCenter = (rectMin + rectMax) * 0.5;
                float2 halfSize   = (rectMax - rectMin) * 0.5;
                float2 p          = IN.worldPosition.xy - rectCenter;
                float  r          = min(_Radius, min(halfSize.x, halfSize.y));
                float2 q          = abs(p) - halfSize + r;
                float  dist       = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                float  aa         = max(fwidth(dist), 1e-4);
                float  coverage   = 1.0 - smoothstep(-aa, aa, dist);
                color.a *= coverage;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
