#ifndef UNITY_SHIM_HLSL_INCLUDED
#define UNITY_SHIM_HLSL_INCLUDED

#define UNITY_VERSION 20220100
#define UNITY_VERSION_MAJOR 2022
#define UNITY_VERSION_MINOR 1
#define SHADER_TARGET 50
#define SHADER_API_D3D11 1
#define SHADER_API_DESKTOP 1
#define UNITY_COMPILER_HLSL 1

#define fixed4 float4
#define fixed3 float3
#define fixed2 float2
#define fixed  float
#define fixed4x4 float4x4
#define fixed3x3 float3x3
#define fixed2x2 float2x2

#define half4  float4
#define half3  float3
#define half2  float2
#define half   float
#define half4x4 float4x4
#define half3x3 float3x3

#define real4  float4
#define real3  float3
#define real2  float2
#define real   float
#define real4x4 float4x4
#define real3x3 float3x3

#define UNITY_PI            3.14159265359f
#define UNITY_TWO_PI        6.28318530718f
#define UNITY_FOUR_PI       12.56637061436f
#define UNITY_INV_PI        0.31830988618f
#define UNITY_INV_TWO_PI    0.15915494309f
#define UNITY_INV_FOUR_PI   0.07957747155f
#define UNITY_HALF_PI       1.57079632679f
#define UNITY_INV_HALF_PI   0.636619772367f
#define PI UNITY_PI
#define TWO_PI UNITY_TWO_PI

#define UNITY_INITIALIZE_OUTPUT(type, name) name = (type)0;
#define UNITY_SETUP_INSTANCE_ID(v)
#define UNITY_TRANSFER_INSTANCE_ID(v, o)
#define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o)
#define UNITY_VERTEX_INPUT_INSTANCE_ID
#define UNITY_VERTEX_OUTPUT_STEREO
#define UNITY_FOG_COORDS(idx)
#define UNITY_TRANSFER_FOG(o, outpos)
#define UNITY_APPLY_FOG(coord, col)
#define UNITY_APPLY_FOG_COLOR(coord, col, fogCol)
#define UNITY_FOG_LERP_COLOR(col, fogColor, fogCoord) col = lerp(fogColor, col, fogCoord);
#define SHADOW_COORDS(idx)
#define TRANSFER_SHADOW(a)
#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
#define UNITY_SETUP_INSTANCE_ID(input)
#define UNITY_TRANSFER_INSTANCE_ID(input, output)
#define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output)
#define UNITY_INSTANCING_BUFFER_START(name)
#define UNITY_INSTANCING_BUFFER_END(name)
#define UNITY_DEFINE_INSTANCED_PROP(type, name) type name
#define UNITY_ACCESS_INSTANCED_PROP(array, name) name
#define UNITY_DECLARE_INSTANCED_PROP(type, name) type name
#define UNITY_BRANCH [branch]
#define UNITY_FLATTEN [flatten]
#define UNITY_LOOP [loop]
#define UNITY_UNROLL [unroll]
#define UNITY_PRECISE precise
#define UNITY_ASSUME_UNIFORM_SCALING
#define UNITY_ASSUME_HOMOGENEOUS_DEPTH
#define UNITY_NEAR_CLIP_VALUE 0.0
#define UNITY_RAW_FAR_CLIP_VALUE 1.0
#define UNITY_MATRIX_P unity_MatrixP
#define UNITY_MATRIX_V unity_MatrixV
#define UNITY_MATRIX_VP unity_MatrixVP
#define UNITY_MATRIX_M unity_ObjectToWorld
#define UNITY_MATRIX_I_M unity_WorldToObject
#define UNITY_MATRIX_MVP glstate_matrix_mvp
#define UNITY_MATRIX_T_MV unity_MatrixT_MV
#define UNITY_MATRIX_IT_MV unity_MatrixT_MV
#define UNITY_MATRIX_MV unity_MatrixVP
#define UNITY_MATRIX_I_V unity_MatrixV
#define UNITY_MATRIX_I_VP unity_MatrixVP
#define UNITY_PREV_MATRIX_M unity_ObjectToWorld
#define UNITY_PREV_MATRIX_I_M unity_WorldToObject

cbuffer ExternalEnginePerFrame : register(b0)
{
    float4x4 unity_ObjectToWorld;
    float4x4 unity_WorldToObject;
    float4x4 unity_MatrixV;
    float4x4 unity_MatrixVP;
    float4x4 unity_MatrixP;
    float4x4 glstate_matrix_mvp;

    float4 _WorldSpaceCameraPos;
    float4 _WorldSpaceLightPos0;
    float4 _LightColor0;
    float4 _MainLightPosition;
    float4 _MainLightColor;

    float4 _Time;
    float4 _SinTime;
    float4 _CosTime;
    float4 unity_DeltaTime;
    float4 _ProjectionParams;
    float4 _ScreenParams;
    float4 unity_OrthoParams;
    float4 unity_SHAr;
    float4 unity_SHAg;
    float4 unity_SHAb;

    float4 _LightPositionRange;
    float4 _LightProjectionParams;
    float4 unity_SpecCube0_HDR;
    float4 unity_AmbientSky;
    float4 unity_AmbientEquator;
    float4 unity_AmbientGround;
    float4 unity_FogColor;
    float4 unity_FogParams;
    float4x4 unity_CameraProjection;
    float4x4 unity_CameraInvProjection;
};

#define unity_MatrixI_V unity_MatrixV

struct appdata_base
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 texcoord : TEXCOORD0;
};

struct appdata_tan
{
    float4 vertex : POSITION;
    float4 tangent : TANGENT;
    float3 normal : NORMAL;
    float4 texcoord : TEXCOORD0;
};

struct appdata_full
{
    float4 vertex : POSITION;
    float4 tangent : TANGENT;
    float3 normal : NORMAL;
    float4 texcoord : TEXCOORD0;
    float4 texcoord1 : TEXCOORD1;
    float4 texcoord2 : TEXCOORD2;
    float4 texcoord3 : TEXCOORD3;
    float4 color : COLOR;
};

#define TEXTURE2D(textureName) Texture2D textureName
#define TEXTURE2D_ARRAY(textureName) Texture2DArray textureName
#define TEXTURE2D_ARGS(textureName, samplerName) Texture2D textureName, SamplerState samplerName
#define TEXTURECUBE(textureName) TextureCube textureName
#define TEXTURECUBE_ARRAY(textureName) TextureCubeArray textureName
#define TEXTURE3D(textureName) Texture3D textureName
#define SAMPLER(samplerName) SamplerState samplerName
#define SAMPLER_CMP(samplerName) SamplerComparisonState samplerName

#define SAMPLE_TEXTURE2D(tex, samp, uv) tex.Sample(samp, uv)
#define SAMPLE_TEXTURE2D_LOD(tex, samp, uv, lod) tex.SampleLevel(samp, uv, lod)
#define SAMPLE_TEXTURE2D_BIAS(tex, samp, uv, bias) tex.SampleBias(samp, uv, bias)
#define SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy) tex.SampleGrad(samp, uv, dx, dy)
#define SAMPLE_TEXTURE2D_ARRAY(tex, samp, uv, index) tex.Sample(samp, float3(uv, index))
#define SAMPLE_TEXTURE2D_ARRAY_LOD(tex, samp, uv, index, lod) tex.SampleLevel(samp, float3(uv, index), lod)
#define SAMPLE_TEXTURECUBE(tex, samp, dir) tex.Sample(samp, dir)
#define SAMPLE_TEXTURECUBE_LOD(tex, samp, dir, lod) tex.SampleLevel(samp, dir, lod)
#define SAMPLE_TEXTURECUBE_ARRAY(tex, samp, dir, index) tex.Sample(samp, float4(dir, index))
#define SAMPLE_TEXTURE3D(tex, samp, uvw) tex.Sample(samp, uvw)
#define SAMPLE_TEXTURE3D_LOD(tex, samp, uvw, lod) tex.SampleLevel(samp, uvw, lod)
#define SAMPLE_DEPTH_TEXTURE(tex, samp, uv) tex.Sample(samp, uv).r

#define UNITY_DECLARE_TEX2D(tex) Texture2D tex; SamplerState sampler##tex
#define UNITY_DECLARE_TEX2D_NOSAMPLER(tex) Texture2D tex
#define UNITY_DECLARE_TEX2D_ARRAY(tex) Texture2DArray tex; SamplerState sampler##tex
#define UNITY_DECLARE_TEXCUBE(tex) TextureCube tex; SamplerState sampler##tex
#define UNITY_SAMPLE_TEX2D(tex, coord) tex.Sample(sampler##tex, coord)
#define UNITY_SAMPLE_TEX2D_LOD(tex, coord, lod) tex.SampleLevel(sampler##tex, coord, lod)
#define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord) tex.Sample(sampler##samp, coord)
#define UNITY_SAMPLE_TEX2D_ARRAY(tex, coord) tex.Sample(sampler##tex, coord)
#define UNITY_SAMPLE_TEXCUBE(tex, coord) tex.Sample(sampler##tex, coord)
#define UNITY_SAMPLE_TEXCUBE_LOD(tex, coord, lod) tex.SampleLevel(sampler##tex, coord, lod)

#define UNITY_DECLARE_DEPTH_TEXTURE(tex) Texture2D tex; SamplerState sampler##tex
#define UNITY_DECLARE_SHADOWMAP(tex) Texture2D tex; SamplerComparisonState sampler##tex
#define UNITY_SAMPLE_DEPTH(depthValue) (depthValue).r
#define UNITY_SAMPLE_SHADOW(tex, coord) tex.SampleCmpLevelZero(sampler##tex, (coord).xy, (coord).z)
#define UNITY_SAMPLE_TEX2DARRAY(tex, coord) tex.Sample(sampler##tex, coord)

#define TRANSFORM_TEX(tex, name) ((tex.xy) * name##_ST.xy + name##_ST.zw)
#define TRANSFORM_OFFSET(offset, name) (offset + name##_ST.zw)

#ifndef UNITY_COLORSPACE_GAMMA
#define UNITY_COLORSPACE_GAMMA 0
#endif
#define UNITY_LIGHTMODEL_AMBIENT (unity_AmbientSky)
#define unity_ColorSpaceGrey float4(0.5, 0.5, 0.5, 0.5)
#define unity_ColorSpaceDouble float4(2.0, 2.0, 2.0, 2.0)
#define unity_ColorSpaceLuminance float4(0.299, 0.587, 0.114, 0.0)

inline float4 UnityObjectToClipPos(in float3 pos)
{
    return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(pos, 1.0)));
}

inline float4 UnityObjectToClipPos(float4 pos)
{
    return UnityObjectToClipPos(pos.xyz);
}

inline float3 UnityObjectToWorldNormal(in float3 norm)
{
    return normalize(mul((float3x3)unity_ObjectToWorld, norm));
}

inline float3 UnityObjectToWorldDir(in float3 dir)
{
    return normalize(mul((float3x3)unity_ObjectToWorld, dir));
}

inline float3 UnityWorldToObjectDir(in float3 dir)
{
    return normalize(mul((float3x3)unity_WorldToObject, dir));
}

inline float3 WorldSpaceViewDir(in float4 localPos)
{
    float3 worldPos = mul(unity_ObjectToWorld, localPos).xyz;
    return _WorldSpaceCameraPos.xyz - worldPos;
}

inline float3 UnityWorldSpaceViewDir(in float3 worldPos)
{
    return _WorldSpaceCameraPos.xyz - worldPos;
}

inline float3 UnityWorldSpaceLightDir(in float3 worldPos)
{
    return _WorldSpaceLightPos0.xyz;
}

inline float3 WorldSpaceLightDir(float4 localPos)
{
    return normalize(_WorldSpaceLightPos0.xyz);
}

inline float3 WorldSpaceLightDir(float3 localPos)
{
    return normalize(_WorldSpaceLightPos0.xyz);
}

inline float3 ObjSpaceLightDir(float4 v)
{
    return normalize(mul((float3x3)unity_WorldToObject, _WorldSpaceLightPos0.xyz));
}

inline float3 ObjSpaceLightDir(float3 v)
{
    return normalize(mul((float3x3)unity_WorldToObject, _WorldSpaceLightPos0.xyz));
}

inline float3 ObjSpaceViewDir(float4 v)
{
    float3 objSpaceCameraPos = mul((float3x3)unity_WorldToObject, _WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, v).xyz);
    return objSpaceCameraPos;
}

inline float3 UnityWorldToViewPos(in float3 pos)
{
    return mul((float3x3)unity_MatrixV, pos);
}

inline float3 UnityObjectToViewPos(in float4 pos)
{
    return UnityWorldToViewPos(mul(unity_ObjectToWorld, pos).xyz);
}

inline float3 UnityObjectToViewPos(in float3 pos)
{
    return UnityObjectToViewPos(float4(pos, 1.0));
}

inline float4 UnityWorldToClipPos(in float3 pos)
{
    return mul(unity_MatrixVP, float4(pos, 1.0));
}

inline float4 UnityViewToClipPos(in float3 pos)
{
    return mul(unity_MatrixP, float4(pos, 1.0));
}

inline float3 UnityViewToWorldPos(in float3 pos)
{
    return mul((float3x3)unity_MatrixV, pos);
}

inline float3 UnityWorldToObject(float3 pos)
{
    return mul(unity_WorldToObject, float4(pos, 1.0)).xyz;
}

inline float4 ComputeScreenPos(float4 pos)
{
    float4 o = pos * 0.5f;
    o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;
    o.zw = pos.zw;
    return o;
}

inline float4 ComputeGrabScreenPos(float4 pos)
{
    return ComputeScreenPos(pos);
}

inline float4 ComputeNonStereoScreenPos(float4 pos)
{
    return ComputeScreenPos(pos);
}

inline float4 UnityPixelSnap(float4 pos)
{
    return pos;
}

inline float4 UnityClipSpaceShadowCasterPos(float4 vertex, float3 normal)
{
    return vertex;
}

inline float4 UnityApplyLinearShadowBias(float4 clipPos)
{
    return clipPos;
}

inline float3 TransformObjectToWorld(float3 positionOS)
{
    return mul(unity_ObjectToWorld, float4(positionOS, 1.0)).xyz;
}

inline float3 TransformWorldToObject(float3 positionWS)
{
    return mul(unity_WorldToObject, float4(positionWS, 1.0)).xyz;
}

inline float4 TransformWorldToHClip(float3 positionWS)
{
    return mul(unity_MatrixVP, float4(positionWS, 1.0));
}

inline float4 TransformObjectToHClip(float3 positionOS)
{
    return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(positionOS, 1.0)));
}

inline float3 TransformObjectToWorldNormal(float3 normalOS, bool doNormalize = true)
{
    float3 normalWS = mul((float3x3)unity_ObjectToWorld, normalOS);
    return doNormalize ? normalize(normalWS) : normalWS;
}

inline float3 TransformObjectToWorldDir(float3 dirOS, bool doNormalize = true)
{
    float3 dirWS = mul((float3x3)unity_ObjectToWorld, dirOS);
    return doNormalize ? normalize(dirWS) : dirWS;
}

inline float3 TransformWorldToViewDir(float3 dirWS, bool doNormalize = true)
{
    float3 dirVS = mul((float3x3)unity_MatrixV, dirWS);
    return doNormalize ? normalize(dirVS) : dirVS;
}

inline float3 GetWorldSpaceNormalizeViewDir(float3 positionWS)
{
    return normalize(_WorldSpaceCameraPos.xyz - positionWS);
}

inline float3 GetCameraPositionWS()
{
    return _WorldSpaceCameraPos.xyz;
}

inline float3 GetMainLightDirection()
{
    return normalize(_WorldSpaceLightPos0.xyz);
}

inline float3 GetMainLightColor()
{
    return _LightColor0.rgb;
}

inline float3 UnpackNormal(float4 packednormal)
{
    return packednormal.xyz * 2.0 - 1.0;
}

inline float3 UnpackNormalScale(float4 packednormal, float bumpScale)
{
    float3 normal = packednormal.xyz * 2.0 - 1.0;
    normal.xy *= bumpScale;
    return normalize(normal);
}

inline float3 UnpackNormalDXT5nm(float4 packednormal)
{
    return UnpackNormal(packednormal);
}

inline float3 UnpackNormalmapRGorAG(float4 packednormal, float scale)
{
    return UnpackNormalScale(packednormal, scale);
}

inline float3 LinearToGammaSpace(float3 linRGB)
{
    return pow(max(linRGB, float3(0.0, 0.0, 0.0)), float3(0.454545, 0.454545, 0.454545));
}

inline float3 LinearToGammaSpaceExact(float3 linRGB)
{
    return LinearToGammaSpace(linRGB);
}

inline float3 GammaToLinearSpace(float3 srgb)
{
    return pow(max(srgb, float3(0.0, 0.0, 0.0)), float3(2.2, 2.2, 2.2));
}

inline float3 GammaToLinearSpaceExact(float3 srgb)
{
    return GammaToLinearSpace(srgb);
}

inline float3 ShadeSH9(float4 normal)
{
    return float3(0.22, 0.25, 0.30);
}

inline float3 ShadeSH9(float3 normal)
{
    return ShadeSH9(float4(normal, 1.0));
}

inline float3 ShadeSH3Order(float4 normal)
{
    return ShadeSH9(normal);
}

inline float3 ShadeSH4Order(float4 normal)
{
    return ShadeSH9(normal);
}

inline float3 Shade4PointLights(
    float4 lightPosX0, float4 lightPosY0, float4 lightPosZ0,
    float4 lightColor0, float4 lightColor1, float4 lightColor2, float4 lightColor3,
    float4 lightAtten0, float3 pos, float3 normal)
{
    return float3(0.0, 0.0, 0.0);
}

inline float3 ShadeVertexLightsFull(float4 vertex, float3 normal, int lightCount, bool spotLight)
{
    return float3(0.0, 0.0, 0.0);
}

inline float3 ShadeVertexLights(float4 vertex, float3 normal)
{
    return float3(0.0, 0.0, 0.0);
}

inline float3 DecodeHDR(float4 data, float4 decodeInstructions)
{
    return data.rgb * decodeInstructions.x;
}

inline float3 DecodeLightmap(float4 tex)
{
    return tex.rgb;
}

inline float3 DecodeRealtimeLightmap(float4 tex)
{
    return tex.rgb;
}

inline float LinearEyeDepth(float z)
{
    return 1.0 / ((1.0 - _ProjectionParams.z / _ProjectionParams.y) * z + (_ProjectionParams.z / _ProjectionParams.y));
}

inline float Linear01Depth(float z)
{
    return LinearEyeDepth(z) / _ProjectionParams.z;
}

inline bool IsEmpty(sampler2D s)
{
    return false;
}

inline bool IsEmpty(samplerCUBE s)
{
    return false;
}

inline float2 UnityStereoScreenSpaceUVAdjust(float2 uv, float4 scaleOffset)
{
    return uv * scaleOffset.xy + scaleOffset.zw;
}

inline float2 UnityStereoScreenSpaceUVAdjust(float2 uv, float2 scaleOffset)
{
    return uv * scaleOffset;
}

inline float2 UnityStereoTransformScreenSpaceTex(float2 uv)
{
    return uv;
}

struct UnityLight
{
    float3 color;
    float3 dir;
    float ndotl;
};

struct UnityIndirect
{
    float3 diffuse;
    float3 specular;
};

struct UnityGI
{
    UnityLight light;
    UnityIndirect indirect;
};

struct UnityGIInput
{
    float4 light;
    float3 worldPos;
    float3 worldViewDir;
    float atten;
    float4 probeHDR[2];
    float4 boxMax[2];
    float4 boxMin[2];
    float4 probePosition[2];
    float4 lightCoord;
    float3 lightColor;
    float3 lightDir;
};

struct Unity_GlossyEnvironmentData
{
    float roughness;
    float3 reflUVW;
};

inline float3 UnityGI_IndirectSpecular(UnityGIInput data, float occlusion, Unity_GlossyEnvironmentData glossIn)
{
    return float3(0.0, 0.0, 0.0);
}

#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) float destName = 1.0;
#define SHADOW_ATTENUATION(a) 1.0
#define LIGHT_ATTENUATION(a) 1.0

inline float UnityGetReceiverShadowFade()
{
    return 1.0;
}

inline float UnitySampleShadow(float4 coord)
{
    return 1.0;
}

float4 unity_4LightPosX0;
float4 unity_4LightPosY0;
float4 unity_4LightPosZ0;
float4 unity_4LightAtten0;
float4 unity_LightColor[8];
float4 unity_LightPosition[8];
float4 unity_LightAtten[8];
float4 unity_SpotDirection[8];
float4 unity_ProbeVolumeSH;
float4 unity_ProbeVolumeParams;
float4 unity_ProbeVolumeMin;
float4 unity_ProbeVolumeSizeInv;
float4 unity_LightShadowBias;
float4 unity_ShadowFadeCenterAndType;
float4 unity_WorldTransformParams;
float4 unity_SHBr;
float4 unity_SHBg;
float4 unity_SHBb;
float4 unity_SHC;
float4 unity_SpecCube1_HDR;
float4 unity_SpecCube0_ProbePosition;
float4 unity_SpecCube1_ProbePosition;
float4x4 unity_SpecCube0_BoxMax;
float4x4 unity_SpecCube0_BoxMin;
float4x4 unity_SpecCube1_BoxMax;
float4x4 unity_SpecCube1_BoxMin;
float4 _LightShadowData;
float4 unity_CameraWorldClipPlanes[6];
sampler2D _ShadowMapTexture;
samplerCUBE unity_SpecCube0;
samplerCUBE unity_SpecCube1;

int unity_InstanceID;

inline float4 AkashicPackBase256(float value)
{
    float4 packed;
    float rest = saturate(value);
    float step;

    step = floor(rest * 255.0);
    packed.x = step / 255.0;
    rest = rest * 255.0 - step;

    step = floor(rest * 255.0);
    packed.y = step / 255.0;
    rest = rest * 255.0 - step;

    step = floor(rest * 255.0);
    packed.z = step / 255.0;
    rest = rest * 255.0 - step;

    packed.w = floor(rest * 255.0) / 255.0;
    return packed;
}

inline float2 EncodeFloatRG(float value)
{
    float4 packed = AkashicPackBase256(value);
    return float2(packed.x, packed.y);
}

inline float DecodeFloatRG(float2 encoded)
{
    return dot(encoded, float2(1.0, 1.0 / 255.0));
}

inline float4 EncodeFloatRGBA(float value)
{
    return AkashicPackBase256(value);
}

inline float DecodeFloatRGBA(float4 encoded)
{
    return dot(encoded, float4(1.0, 1.0 / 255.0, 1.0 / 65025.0, 1.0 / 16581375.0));
}

inline float2 DecodeHalf2ToFloat(float2 encoded)
{
    return encoded;
}

inline float2 EncodeFloatToHalf2(float value)
{
    return float2(value, 0.0);
}

#define CBUFFER_START(name)
#define CBUFFER_END
#define CBUFFER_START_WITH_INSTANCING(name)

#endif
