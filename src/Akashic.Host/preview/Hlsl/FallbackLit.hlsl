#include "UnityShim.hlsl"

float4 _Color;
sampler2D _MainTex;
float4 _MainTex_ST;
sampler2D _BumpMap;
float _BumpScale;
float4 _EmissionColor;
sampler2D _EmissionMap;
float4 _EmissionMap_ST;
float _Cutoff;

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
    float3 normal : TEXCOORD1;
    float3 tangent : TEXCOORD2;
    float3 bitangent : TEXCOORD3;
    float3 worldPos : TEXCOORD4;
};

v2f vert(appdata v)
{
    v2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
    o.normal = UnityObjectToWorldNormal(v.normal);
    o.tangent = normalize(UnityObjectToWorldDir(v.tangent.xyz));
    o.bitangent = cross(o.normal, o.tangent) * v.tangent.w;
    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
    return o;
}

float4 frag(v2f i) : SV_Target
{
#ifdef AKASHIC_GIZMO
    return _Color;
#endif

#ifdef AKASHIC_WIREFRAME
#ifdef AKASHIC_LIGHT
    return float4(0.14, 0.24, 0.55, 1.0);
#else
    return float4(0.72, 0.80, 0.95, 1.0);
#endif
#endif

#ifdef AKASHIC_CLAY
    float3 clayNormal = normalize(i.normal);
    float3 clayLight = normalize(_WorldSpaceLightPos0.xyz);
    float clayDiffuse = saturate(abs(dot(clayNormal, clayLight)));
#ifdef AKASHIC_LIGHT
    return float4(lerp(float3(0.20, 0.22, 0.26), float3(0.62, 0.64, 0.68), clayDiffuse), 1.0);
#else
    return float4(lerp(float3(0.34, 0.36, 0.40), float3(0.94, 0.95, 0.97), clayDiffuse), 1.0);
#endif
#endif

    float4 color = tex2D(_MainTex, i.uv) * _Color;

#ifdef _ALPHATEST_ON
    clip(color.a - max(_Cutoff, 0.001));
#endif

    float3 normal = normalize(i.normal);
    float3 tangent = normalize(i.tangent - normal * dot(normal, i.tangent));
    float3 bitangent = normalize(cross(normal, tangent));

    float3 bump = UnpackNormalScale(tex2D(_BumpMap, i.uv), _BumpScale);
    normal = normalize(tangent * bump.x + bitangent * bump.y + normal * bump.z);

    float3 light = normalize(_WorldSpaceLightPos0.xyz);
    float diffuse = saturate(dot(normal, light) * 0.5 + 0.5);
    diffuse = lerp(0.62, 1.0, diffuse);

    float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
    float rim = pow(1.0 - saturate(dot(normal, viewDir)), 3.0) * 0.08;

    float3 result = color.rgb * (diffuse + rim);
    result += tex2D(_EmissionMap, i.uv * _EmissionMap_ST.xy + _EmissionMap_ST.zw).rgb * _EmissionColor.rgb;

    return float4(result, color.a);
}
