Shader "Custom/GroundGrassWindInteract"
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
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float windAmount = v.uv2.x;
                float phase = v.uv2.y;

                float wind =
                    sin(
                        _Time.y * _WindSpeed +
                        worldPos.x * _WindScale +
                        worldPos.z * _WindScale +
                        phase
                    );

                float2 windDir = normalize(float2(0.75, 0.35));
                worldPos.xz += windDir * wind * _WindStrength * windAmount;

                float2 toBlade = worldPos.xz - _PlayerPosition.xz;
                float dist = length(toBlade);

                float pushMask = saturate(1.0 - dist / max(_PushRadius, 0.001));
                pushMask *= pushMask;

                float2 pushDir = dist > 0.001 ? toBlade / dist : float2(0.0, 0.0);

                worldPos.xz += pushDir * pushMask * _PushStrength * windAmount;
                worldPos.y -= pushMask * _FlattenStrength * windAmount;

                o.position = UnityWorldToClipPos(worldPos);
                o.color = v.color * _Brightness;

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