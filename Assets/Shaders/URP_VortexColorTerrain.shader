Shader "Custom/URP Vertex Color Terrain"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 2)) = 1
        _MainTex ("Optional Texture", 2D) = "white" {}
        _TextureStrength ("Texture Strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode"="UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Brightness;
                float _TextureStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();

                half3 normalWS = normalize(input.normalWS);
                half lightAmount = saturate(dot(normalWS, mainLight.direction));

                // Keeps it stylized, not pitch black.
                lightAmount = lightAmount * 0.55h + 0.45h;

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half3 baseColor = lerp(
                    input.color.rgb,
                    input.color.rgb * tex.rgb,
                    _TextureStrength
                );

                baseColor *= mainLight.color.rgb;
                baseColor *= lightAmount;
                baseColor *= _Brightness;

                return half4(baseColor, 1);
            }

            ENDHLSL
        }
    }
}