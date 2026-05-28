Shader "Custom/GroundGrassWindInteract_Lit"
{
    Properties
    {
        _WindStrength ("Wind Strength", Float) = 0.08
        _WindSpeed ("Wind Speed", Float) = 1.6
        _WindScale ("Wind Scale", Float) = 0.45

        _PlayerPosition ("Player Position", Vector) = (0, 0, 0, 0)
        _PushRadius ("Player Push Radius", Float) = 1.6
        _PushStrength ("Player Push Strength", Float) = 0.30
        _FlattenStrength ("Player Flatten Strength", Float) = 0.08

        _Brightness ("Brightness", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Cull Off
        ZWrite On
        ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _WindStrength;
            float _WindSpeed;
            float _WindScale;

            float4 _PlayerPosition;
            float _PushRadius;
            float _PushStrength;
            float _FlattenStrength;

            float _Brightness;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Wind & Player Interaction
                float wind = sin(_Time.y * _WindSpeed + worldPos.x * _WindScale + worldPos.z * _WindScale + v.uv2.y);
                float2 windDir = normalize(float2(0.75, 0.35));
                worldPos.xz += windDir * wind * _WindStrength * v.uv2.x;

                float2 toBlade = worldPos.xz - _PlayerPosition.xz;
                float dist = length(toBlade);
                float pushMask = saturate(1.0 - dist / max(_PushRadius, 0.001));
                pushMask *= pushMask;
                float2 pushDir = dist > 0.001 ? toBlade / dist : float2(0,0);
                worldPos.xz += pushDir * pushMask * _PushStrength * v.uv2.x;
                worldPos.y -= pushMask * _FlattenStrength * v.uv2.x;

                // Lighting
                float3 normalWS = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float NdotL = max(0, dot(normalWS, normalize(_WorldSpaceLightPos0.xyz)));

                o.pos = UnityWorldToClipPos(float4(worldPos,1));
                o.color = v.color * _Brightness * NdotL;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}