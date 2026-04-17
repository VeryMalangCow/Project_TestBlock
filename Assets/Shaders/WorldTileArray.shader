Shader "Project/World/TileArray"
{
    Properties
    {
        _MainTex ("Texture Array", 2DArray) = "" {}
        _Color ("Main Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "Queue" = "Geometry" 
            "RenderPipeline" = "UniversalPipeline" 
        }
        LOD 100

        Pass
        {
            // Opaque doesn't need blend, but we use Alpha Clipping
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv : TEXCOORD0; 
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float slotIdx : TEXCOORD1;
                nointerpolation float ruleId : TEXCOORD4;
                float2 lightUV : TEXCOORD3;
                float4 color : COLOR;
            };

            // ----------------------------------------------------------------
            // SRP Batcher Compatibility
            // ----------------------------------------------------------------
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color; // Add color to properties to ensure CBUFFER is valid
            CBUFFER_END

            // Global Properties (Outside CBUFFER - Shared across all chunks)
            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_WorldLightMap);
            SAMPLER(sampler_WorldLightMap);
            
            float4 _WorldLightSettings; 
            // ----------------------------------------------------------------
            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                output.uv = input.uv.xy;
                output.slotIdx = input.uv.z;
                output.ruleId = input.uv.w;
                output.color = input.color;
                
                // Calculate Light UV from World Position
                float3 worldPos = vertexInput.positionWS;
                output.lightUV = worldPos.xy / max(_WorldLightSettings.xy, 1.0);
                
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                // 1. Calculate Atlas UV (8x8 Grid)
                float col = floor(input.ruleId % 8.0);
                float row = 7.0 - floor(input.ruleId / 8.0);
                
                float2 atlasUV = (input.uv + float2(col, row)) / 8.0;

                // 2. Sample Tile Array
                float4 texCol = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, atlasUV, input.slotIdx);
                if(texCol.a < 0.01) discard;

                // 3. Sample World Light Map (Linear/Bilinear)
                float4 light = SAMPLE_TEXTURE2D(_WorldLightMap, sampler_WorldLightMap, input.lightUV);

                // 4. Combine
                return texCol * float4(light.rrr, 1.0) * input.color;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
