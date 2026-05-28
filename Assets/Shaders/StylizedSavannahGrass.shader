Shader "Custom/StylizedSavannahGrassURP_Lit"
{
    Properties
    {
        _MainTex ("Grass Texture", 2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.35

        _WindStrength ("Wind Strength", Range(0,1)) = 0.18
        _WindSpeed ("Wind Speed", Range(0,10)) = 2.4
        _WindScale ("Wind Scale", Range(1,80)) = 18

        _Brightness ("Brightness", Range(0,2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
        }

        Cull Off
        ZWrite On
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _AlphaCutoff;
                float _WindStrength;
                float _WindSpeed;
                float _WindScale;
                float _Brightness;
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
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);

                float windNoise = sin(worldPos.x / _WindScale + _Time.y * _WindSpeed) *
                                  cos(worldPos.z / _WindScale + _Time.y * _WindSpeed * 0.7);
                float windWeight = input.color.r;
                worldPos.x += windNoise * _WindStrength * windWeight;

                output.positionHCS = TransformObjectToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(tex.a - _AlphaCutoff);

                half3 baseCol = tex.rgb * input.color.rgb;
                baseCol *= mainLight.color.rgb;
                baseCol *= NdotL * _Brightness;

                return half4(baseCol, 1);
            }
            ENDHLSL
        }
    }
}