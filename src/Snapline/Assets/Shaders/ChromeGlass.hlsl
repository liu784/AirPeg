// Original Snapline chrome shader; MIT. Never applied to a screenshot or text.
sampler2D Backdrop : register(s0);
float2 Size : register(c0);
float Density : register(c1);
float Dark : register(c2);
float TextSafe : register(c3);
float4 main(float2 uv : TEXCOORD) : COLOR {
    float2 dimensions = max(Size, float2(1,1));
    float2 edge = min(uv, 1-uv) * dimensions;
    float band = pow(1-saturate(min(edge.x, edge.y)/22), 2);
    float2 normal = edge.x < edge.y ? float2(sign(uv.x-.5),0) : float2(0,sign(uv.y-.5));
    float2 shift = normal * band * (1.4 + Density*2.6) / dimensions;
    float2 at = saturate(uv - shift);
    float2 blur = (0.6 + Density*0.6) / dimensions;
    float3 sum = 0;
    float weights[5] = { .0625, .25, .375, .25, .0625 };
    [unroll] for (int y=0;y<5;y++) {
        [unroll] for (int x=0;x<5;x++) {
            sum += tex2D(Backdrop, saturate(at + float2(x-2,y-2)*blur)).rgb * weights[x]*weights[y];
        }
    }
    // Subpixel chromatic separation only in the rim, not a rainbow outline.
    float3 rim = float3(tex2D(Backdrop,saturate(at-shift*.07)).r,tex2D(Backdrop,at).g,tex2D(Backdrop,saturate(at+shift*.07)).b);
    sum = lerp(sum,rim,band*.14);
    if (TextSafe > .5) {
        // Coarse area averages have already removed desktop glyph detail.
        // Preserve atmosphere rather than painting a uniform white sheet.
        float3 neutral = dot(sum,float3(.2126,.7152,.0722));
        sum = lerp(sum,neutral,.35);
        float3 tone = lerp(float3(.985,.987,.990),float3(.055,.065,.078),Dark);
        sum = lerp(sum,tone,lerp(.22+Density*.30,.34+Density*.32,Dark));
        float luminance = dot(sum,float3(.2126,.7152,.0722));
        if (Dark < .5) sum += max(0,.61-luminance);
        else sum *= min(1,.23/max(luminance,.001));
        // Directional neutral glancing light, confined to the outer bevel.
        float reflection=band*(.065+.045*Density)*(.35+.65*(1-uv.y));
        return float4(saturate(sum+reflection*(1-Dark*.25)),1);
    }
    float3 tint = lerp(float3(.94,.965,.99),float3(.065,.092,.13),Dark);
    sum=lerp(sum,tint,.50+Density*.32);
    float light = band * (.025 + .025*(1-uv.y)) * (1-Dark*.35);
    return float4(saturate(sum+light),1);
}
