Shader "UI/ItemIconArrayBatch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MainTexArray ("Icon Array", 2DArray) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 uv2      : TEXCOORD1; // [Batch] 슬롯 인덱스를 담는 통로
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float sliceIndex : TEXCOORD1; // [Batch] 버텍스에서 넘어온 인덱스
            };

            fixed4 _Color;
            UNITY_DECLARE_TEX2DARRAY(_MainTexArray);

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                o.sliceIndex = v.uv2.x; // [Batch] uv2의 x값을 인덱스로 사용
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color;
                
                if (i.sliceIndex < 0)
                {
                    color = fixed4(0, 0, 0, 0);
                }
                else
                {
                    color = UNITY_SAMPLE_TEX2DARRAY(_MainTexArray, float3(i.texcoord, i.sliceIndex)) * i.color;
                }

                return color;
            }
            ENDCG
        }
    }
}
