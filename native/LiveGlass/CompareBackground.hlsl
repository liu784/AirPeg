// Original P5 change detector. Does not render or modify the accepted optical material.
cbuffer Parameters : register(b0) { float4 Frame; float4 Crop; float4 Blur; float4 Style; float4 Content; };
Texture2D Current : register(t0);
Texture2D Previous : register(t1);
struct VertexOut { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
float4 PSCompare(VertexOut input) : SV_TARGET
{
    int2 p = clamp(int2(input.position.xy) + int2(Crop.xy) - 8, int2(0,0), int2(Frame.xy)-1);
    if (all(Current.Load(int3(p,0)).rgb == Previous.Load(int3(p,0)).rgb)) discard;
    return float4(0,0,0,0);
}
