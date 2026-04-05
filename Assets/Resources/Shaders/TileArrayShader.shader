Shader "Custom/TileArrayShader"
{
    Properties
    {
        _MainTex ("Texture Array", 2DArray) = "" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 uv : TEXCOORD0; // uv.z stores the layer index
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float layer : TEXCOORD1; // Optimization: Disable interpolation for layer index
                float4 color : COLOR;
            };

            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex); // Standard naming to avoid compile errors

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv.xy;
                output.layer = input.uv.z;
                output.color = input.color;
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                // Sampling with no-interpolation layer index
                float4 col = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, input.uv, input.layer);
                
                // Optimization: Discard transparent pixels to reduce overdraw
                if(col.a < 0.01) discard;

                return col * input.color;
            }
            ENDHLSL
        }
    }
}
