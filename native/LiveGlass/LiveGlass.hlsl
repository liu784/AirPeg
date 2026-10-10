// Original GPU material for Tendedero Windows. No third-party shader source.
cbuffer Parameters : register(b0)
{
    float4 Frame;        // desktop width/height, current target width/height
    float4 Crop;         // full-resolution window crop within desktop output
    float4 Blur;         // pixel axis x/y, sigma in filtered pixels, reserved
    float4 Style;        // density, dark, reserved, reserved
    float4 Content;      // reserved ABI layout; no region mask
};
Texture2D Source : register(t0);
Texture2D Desktop : register(t1);
SamplerState ClampLinear : register(s0);
struct VertexOut { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
VertexOut VS(uint id : SV_VertexID)
{
    VertexOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.position = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float4 PSDownsample(VertexOut input) : SV_TARGET
{
    // A 4x4 area prefilter for each quarter-resolution pixel. Fine desktop
    // glyph pixels are filtered before decimation, avoiding moire/grid alias.
    float2 uv = (Crop.xy + input.uv * Crop.zw) / Frame.xy;
    float2 step = Crop.zw / (Frame.zw * 4 * Frame.xy);
    float3 color = 0;
    [unroll] for (int y = 0; y < 4; y++)
        [unroll] for (int x = 0; x < 4; x++)
            color += Source.SampleLevel(ClampLinear, uv + float2(x - 1.5, y - 1.5) * step, 0).rgb;
    return float4(color / 16, 1);
}

float4 PSBlur(VertexOut input) : SV_TARGET
{
    // Continuous Gaussian on the already filtered quarter surface. Adjacent
    // taps are combined with exact bilinear weights, never spaced 7-16 pixels.
    float sigma = max(Blur.z, 0.6);
    float2 texel = Blur.xy / Frame.zw;
    float3 color = Source.SampleLevel(ClampLinear, input.uv, 0).rgb;
    float total = 1;
    float radius = min(48, ceil(3 * sigma));
    [loop] for (int i = 1; i <= 48; i += 2)
    {
        if (i > radius) break;
        float a = exp(-0.5 * (i / sigma) * (i / sigma));
        float b = i + 1 <= radius ? exp(-0.5 * ((i + 1) / sigma) * ((i + 1) / sigma)) : 0;
        float weight = a + b;
        float offset = i + b / weight;
        color += (Source.SampleLevel(ClampLinear, input.uv + offset * texel, 0).rgb +
                  Source.SampleLevel(ClampLinear, input.uv - offset * texel, 0).rgb) * weight;
        total += 2 * weight;
    }
    return float4(color / total, 1);
}

float4 PSCompose(VertexOut input) : SV_TARGET
{
    float2 p = input.uv * Frame.zw;
    float2 edgeDistance = min(p, Frame.zw - p);
    float edge = min(edgeDistance.x, edgeDistance.y);
    float rim = 1 - smoothstep(0, 10, edge);
    float2 normal = edgeDistance.x <= edgeDistance.y
        ? float2(p.x < Frame.z / 2 ? 1 : -1, 0)
        : float2(0, p.y < Frame.w / 2 ? 1 : -1);
    float2 refracted = input.uv + normal * rim * (2.0 + 2.0 * Style.x) / Frame.zw;
    float2 desktopUV = (Crop.xy + refracted * Crop.zw) / Frame.xy;
    float3 sharp = Desktop.SampleLevel(ClampLinear, desktopUV, 0).rgb;
    float3 softened = Source.SampleLevel(ClampLinear, refracted, 0).rgb;
    // One continuous material over the entire window. These original product
    // values approximate clear glass; they are not Apple renderer parameters.
    // No content mask, neutral tint, luma floor/ceiling, or reading protection.
    float3 color = lerp(sharp, softened, 0.08 + 0.52 * Style.x);
    float light = (normal.x + normal.y) > 0 ? 1 : 0.35;
    float fresnel = rim * rim * (0.09 + 0.07 * Style.x);
    color = lerp(color, float3(1, 1, 1), fresnel * light);
    // Opaque composed desktop pixels, not an opaque white overlay.
    return float4(saturate(color), 1);
}
