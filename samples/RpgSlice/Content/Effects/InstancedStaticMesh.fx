float4x4 ViewProjection;
float3 CameraPosition;
float3 LightDirection;
float3 DirectionalLightColor;
float3 AmbientLightColor;
float3 FogColor;
float FogStart;
float FogEnd;

struct InstancedInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
    float4 WorldRow0 : TEXCOORD1;
    float4 WorldRow1 : TEXCOORD2;
    float4 WorldRow2 : TEXCOORD3;
    float4 WorldRow3 : TEXCOORD4;
    float4 NormalRow0 : TEXCOORD5;
    float4 NormalRow1 : TEXCOORD6;
    float4 NormalRow2 : TEXCOORD7;
    float4 Tint : COLOR1;
};

struct InstancedOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
};

InstancedOutput InstancedVertex(InstancedInput input)
{
    InstancedOutput output;
    float4x4 world = float4x4(input.WorldRow0, input.WorldRow1,
        input.WorldRow2, input.WorldRow3);
    float3x3 normalMatrix = float3x3(input.NormalRow0.xyz,
        input.NormalRow1.xyz, input.NormalRow2.xyz);
    float4 worldPosition = mul(input.Position, world);
    float3 worldNormal = normalize(mul(input.Normal, normalMatrix));
    float diffuse = saturate(dot(worldNormal, -normalize(LightDirection)));
    float3 lighting = AmbientLightColor + DirectionalLightColor * diffuse;
    float fog = saturate((distance(worldPosition.xyz, CameraPosition) - FogStart)
        / (FogEnd - FogStart));

    output.Position = mul(worldPosition, ViewProjection);
    output.Color = float4(lerp(input.Tint.rgb * lighting, FogColor, fog), input.Tint.a);
    return output;
}

float4 InstancedPixel(InstancedOutput input) : COLOR0
{
    return input.Color;
}

technique InstancedStaticMesh
{
    pass P0
    {
        VertexShader = compile vs_4_0 InstancedVertex();
        PixelShader = compile ps_4_0 InstancedPixel();
    }
}
