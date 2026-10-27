Shader "Akashic/Standard"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _BumpScale ("Scale", Float) = 1.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _OcclusionStrength ("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _Mode ("__mode", Float) = 0.0
        _SrcBlend ("__src", Float) = 1.0
        _DstBlend ("__dst", Float) = 0.0
        _ZWrite ("__zw", Float) = 1.0
        _Cull ("__cull", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="ForwardBase" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature _ALPHATEST_ON
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Cutoff;
            float _Glossiness;
            float _Metallic;
            sampler2D _MetallicGlossMap;
            float _BumpScale;
            sampler2D _BumpMap;
            float _OcclusionStrength;
            sampler2D _OcclusionMap;
            float4 _EmissionColor;
            sampler2D _EmissionMap;
            float4 _EmissionMap_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldTangent : TEXCOORD2;
                float3 worldBitangent : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float DistributionGGX(float NdotH, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a;
                float d = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / max(UNITY_PI * d * d, 1e-6);
            }

            float GeometrySmith(float NdotV, float NdotL, float roughness)
            {
                float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
                float gv = NdotV / (NdotV * (1.0 - k) + k);
                float gl = NdotL / (NdotL * (1.0 - k) + k);
                return gv * gl;
            }

            float3 FresnelSchlick(float cosTheta, float3 f0)
            {
                return f0 + (1.0 - f0) * pow(saturate(1.0 - cosTheta), 5.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 albedoTex = tex2D(_MainTex, i.uv);
                float4 albedo = albedoTex * _Color;

                #ifdef _ALPHATEST_ON
                clip(albedo.a - max(_Cutoff, 0.001));
                #endif

                float3 N = normalize(i.worldNormal);
                float3 T = normalize(i.worldTangent - N * dot(N, i.worldTangent));
                float3 B = normalize(cross(N, T));
                float3 normalSample = UnpackNormalScale(tex2D(_BumpMap, i.uv), _BumpScale);
                N = normalize(T * normalSample.x + B * normalSample.y + N * normalSample.z);

                float4 metallicSample = tex2D(_MetallicGlossMap, i.uv);
                float metallic = saturate(_Metallic * metallicSample.r);
                float smoothness = saturate(_Glossiness * metallicSample.a);
                float roughness = max(1.0 - smoothness, 0.04);

                float occlusion = lerp(1.0, tex2D(_OcclusionMap, i.uv).g, saturate(_OcclusionStrength));

                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(N, L));
                float NdotV = saturate(dot(N, V));
                float NdotH = saturate(dot(N, H));
                float VdotH = saturate(dot(V, H));

                float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo.rgb, metallic);
                float3 F = FresnelSchlick(VdotH, f0);
                float D = DistributionGGX(NdotH, roughness);
                float G = GeometrySmith(NdotV, NdotL, roughness);
                float3 specular = D * G * F / max(4.0 * NdotV * NdotL, 1e-4);

                float3 kd = (1.0 - F) * (1.0 - metallic);
                float3 lightColor = _LightColor0.rgb;
                float3 diffuse = kd * albedo.rgb;
                float3 direct = (diffuse + specular) * lightColor * NdotL;

                float3 ambient = ShadeSH9(float4(N, 1.0)) * albedo.rgb;
                float3 emission = _EmissionColor.rgb * tex2D(_EmissionMap, i.uv * _EmissionMap_ST.xy + _EmissionMap_ST.zw).rgb;

                float3 finalColor = (direct + ambient) * occlusion + emission;
                return float4(finalColor, albedo.a);
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
