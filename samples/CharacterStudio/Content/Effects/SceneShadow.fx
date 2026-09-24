float4x4 World;
float4x4 WorldInverseTranspose;
float4x4 View;
float4x4 Projection;
float4x4 LightViewProjection;
float4x4 BoneTransforms[72];

texture BaseTexture;
texture ShadowTexture;

sampler BaseSampler = sampler_state
{
    Texture = <BaseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float4 MaterialColor = float4(1, 1, 1, 1);
float3 AmbientColor = float3(0.62, 0.64, 0.68);
float3 DirectionalDirection = float3(-0.4, -1, -0.25);
float3 DirectionalColor = float3(0.9, 0.9, 0.9);
float ShadowTexelSize = 0.001;
float ShadowDepthBias = 0.0015;
bool BaseTextureEnabled = false;
bool AlphaCutoutEnabled = false;
float AlphaCutoff = 0.5;

struct StaticInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
};

struct SkinnedInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
    float4 BlendIndices : BLENDINDICES0;
    float4 BlendWeights : BLENDWEIGHT0;
};

struct DepthOutput
{
    float4 Position : POSITION0;
    float Depth : TEXCOORD0;
    float2 TextureCoordinate : TEXCOORD1;
};

struct SceneOutput
{
    float4 Position : POSITION0;
    float4 LightPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TextureCoordinate : TEXCOORD2;
};

DepthOutput DepthStaticVertex(StaticInput input)
{
    DepthOutput output;
    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(worldPosition, LightViewProjection);
    output.Depth = output.Position.z / output.Position.w;
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

float4 PackDepth(float depth)
{
    float4 shifts = float4(1.0, 255.0, 65025.0, 16581375.0);
    float4 mask = float4(1.0 / 255.0, 1.0 / 255.0, 1.0 / 255.0, 0.0);
    float4 packed = frac(saturate(depth) * shifts);
    packed -= packed.yzww * mask;
    return packed;
}

float4 DepthPixel(DepthOutput input) : COLOR0
{
    float alpha = MaterialColor.a;
    if (BaseTextureEnabled) alpha *= tex2D(BaseSampler, input.TextureCoordinate).a;
    if (AlphaCutoutEnabled) clip(alpha - AlphaCutoff);
    return PackDepth(input.Depth);
}

float4x4 SkinTransform(SkinnedInput input)
{
    return BoneTransforms[(int)input.BlendIndices.x] * input.BlendWeights.x
        + BoneTransforms[(int)input.BlendIndices.y] * input.BlendWeights.y
        + BoneTransforms[(int)input.BlendIndices.z] * input.BlendWeights.z
        + BoneTransforms[(int)input.BlendIndices.w] * input.BlendWeights.w;
}

DepthOutput DepthSkinnedVertex(SkinnedInput input)
{
    DepthOutput output;
    float4 skinnedPosition = mul(input.Position, SkinTransform(input));
    float4 worldPosition = mul(skinnedPosition, World);
    output.Position = mul(worldPosition, LightViewProjection);
    output.Depth = output.Position.z / output.Position.w;
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

SceneOutput SceneStaticVertex(StaticInput input)
{
    SceneOutput output;
    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.LightPosition = mul(worldPosition, LightViewProjection);
    output.Normal = normalize(mul(input.Normal, (float3x3)WorldInverseTranspose));
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

SceneOutput SceneSkinnedVertex(SkinnedInput input)
{
    SceneOutput output;
    float4x4 skin = SkinTransform(input);
    float4 skinnedPosition = mul(input.Position, skin);
    float4 worldPosition = mul(skinnedPosition, World);
    float3 skinnedNormal = mul(input.Normal, (float3x3)skin);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.LightPosition = mul(worldPosition, LightViewProjection);
    output.Normal = normalize(mul(skinnedNormal, (float3x3)WorldInverseTranspose));
    output.TextureCoordinate = input.TextureCoordinate;
    return output;
}

float UnpackDepth(float4 packed)
{
    return dot(packed, float4(1.0, 1.0 / 255.0, 1.0 / 65025.0, 1.0 / 16581375.0));
}

float ShadowVisibility(float4 lightPosition)
{
    float3 projected = lightPosition.xyz / lightPosition.w;
    float2 uv = float2(projected.x * 0.5 + 0.5, -projected.y * 0.5 + 0.5);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0
        || projected.z < 0.0 || projected.z > 1.0)
        return 1.0;

    float visibility = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float storedDepth = UnpackDepth(tex2D(ShadowSampler,
                uv + float2(x, y) * ShadowTexelSize));
            visibility += projected.z - ShadowDepthBias <= storedDepth ? 1.0 : 0.0;
        }
    }
    return lerp(0.35, 1.0, visibility / 9.0);
}

float4 ScenePixel(SceneOutput input) : COLOR0
{
    float4 baseColor = MaterialColor;
    if (BaseTextureEnabled) baseColor *= tex2D(BaseSampler, input.TextureCoordinate);
    if (AlphaCutoutEnabled) clip(baseColor.a - AlphaCutoff);

    float diffuse = saturate(dot(normalize(input.Normal), normalize(-DirectionalDirection)));
    float visibility = ShadowVisibility(input.LightPosition);
    float3 lighting = AmbientColor + DirectionalColor * diffuse * visibility;
    return float4(baseColor.rgb * lighting, 1.0);
}

technique StaticDepth
{
    pass P0
    {
        VertexShader = compile vs_4_0 DepthStaticVertex();
        PixelShader = compile ps_4_0 DepthPixel();
    }
}

technique SkinnedDepth
{
    pass P0
    {
        VertexShader = compile vs_4_0 DepthSkinnedVertex();
        PixelShader = compile ps_4_0 DepthPixel();
    }
}

technique StaticScene
{
    pass P0
    {
        VertexShader = compile vs_4_0 SceneStaticVertex();
        PixelShader = compile ps_4_0 ScenePixel();
    }
}

technique SkinnedScene
{
    pass P0
    {
        VertexShader = compile vs_4_0 SceneSkinnedVertex();
        PixelShader = compile ps_4_0 ScenePixel();
    }
}
