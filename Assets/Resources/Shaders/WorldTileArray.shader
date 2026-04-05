Shader "Project/World/TileArray"
{
    Properties
    {
        _MainTex ("Texture Array", 2DArray) = "" {}
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
        }
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
                float3 uv : TEXCOORD0; 
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float layer : TEXCOORD1;
                float2 lightUV : TEXCOORD3;
                float4 color : COLOR;
            };

            // Global Properties
            TEXTURE2D_ARRAY(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_WorldLightMap);
            SAMPLER(sampler_WorldLightMap);
            
            float4 _WorldLightSettings; 

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                output.uv = input.uv.xy;
                output.layer = input.uv.z;
                output.color = input.color;
                
                // Calculate Light UV from World Position
                float3 worldPos = vertexInput.positionWS;
                output.lightUV = worldPos.xy / max(_WorldLightSettings.xy, 1.0);
                
                return output;
            }

            float4 frag (Varyings input) : SV_Target
            {
                // 1. Sample Tile Array
                float4 col = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, input.uv, input.layer);
                if(col.a < 0.01) discard;

                // 2. Sample World Light Map (Linear/Bilinear)
                float4 light = SAMPLE_TEXTURE2D(_WorldLightMap, sampler_WorldLightMap, input.lightUV);

                // 3. Combine
                return col * float4(light.rrr, 1.0) * input.color;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
