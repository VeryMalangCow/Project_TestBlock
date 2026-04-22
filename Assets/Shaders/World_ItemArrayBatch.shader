Shader "World/ItemArrayBatch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MainTexArray ("Icon Array", 2DArray) = "white" {}
        _SliceIndex ("Slice Index", Float) = -1
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing // GPU 인스턴싱 활성화

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 인스턴스 ID 입력
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 인스턴스 ID 전달
            };

            fixed4 _Color;
            UNITY_DECLARE_TEX2DARRAY(_MainTexArray);
            
            // 인스턴스마다 다를 수 있는 속성 정의 (_SliceIndex)
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _SliceIndex)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float sliceIdx = UNITY_ACCESS_INSTANCED_PROP(Props, _SliceIndex);

                if (sliceIdx < 0) return fixed4(0,0,0,0);
                
                return UNITY_SAMPLE_TEX2DARRAY(_MainTexArray, float3(i.texcoord, sliceIdx)) * i.color;
            }
            ENDCG
        }
    }
}
