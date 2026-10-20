// Display-only recoloring shared by default, Break and Hold shine materials.
// The caller retains its normal texture, vertex alpha and shine animation.
float4 _NoteColor;
float _NoteAlpha;
float4 _NoteSize;
float3 AlphaRGBToHSV(float3 c)
{
    float4 K = float4(0, -1.0 / 3.0, 2.0 / 3.0, -1);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    return float3(abs(q.z + (q.w - q.y) / (6 * d + 1e-9)), d / (q.x + 1e-9), q.x);
}
float3 AlphaHSVToRGB(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3);
    return c.z * lerp(float3(1, 1, 1), saturate(p - 1), c.y);
}
float4 AlphaTint(float4 source)
{
    if (_NoteColor.a > 0)
    {
        float brightness = max(1, max(source.r, max(source.g, source.b)));
        float3 rgb = source.rgb / brightness;
        float3 src = AlphaRGBToHSV(rgb);
        float3 dst = AlphaRGBToHSV(_NoteColor.rgb);
        float detail = 0.35 * (1 - dst.z) * (src.z - 0.5);
        float value = saturate(src.z * dst.z + detail);
        float3 tinted = AlphaHSVToRGB(float3(dst.x, src.y * dst.y, value));
        float guard = smoothstep(0.04, 0.16, src.z);
        float chroma = smoothstep(0.05, 0.25, src.y);
        float gray = 1 - smoothstep(0.05, 0.2, dst.y);
        float3 colored = lerp(rgb, tinted, chroma);
        colored = lerp(colored, float3(value, value, value), gray);
        source.rgb = lerp(rgb, colored, guard * _NoteColor.a) * brightness;
    }
    source.a *= _NoteAlpha;
    return source;
}
