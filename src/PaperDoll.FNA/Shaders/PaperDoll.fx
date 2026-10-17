// PaperDoll.FNA's and PaperDoll.MonoGame's effect. The vertex shader maps puppet-space positions to clip space. The
// pixel shaders draw a Part (screen tint, tint and opacity on the premultiplied texel, transparent outside the
// texture's area), a mask source into the stencil buffer (where its alpha is above its threshold), and the advanced
// blend modes, which read a copy of the layer below and apply the equations of OpenGL's KHR_blend_equation_advanced.
//
// Compiled with fxc (fx_2_0) by build-shaders.ps1 into PaperDoll.fxb for FNA, and with mgfxc by
// src/PaperDoll.MonoGame/Shaders/build-shaders.ps1 into one MGFX effect per MonoGame backend. The compiled files are
// committed and embedded; building PaperDoll needs no shader compiler.

float4x4 Transform;    // vertex positions to clip space
float2 PixelOffset;    // added to backdrop coordinates so they land inside the pixel's texel (see PuppetRenderer)
float2 BackdropScale;  // the render target's size over the backdrop's (the backdrop may be larger)

texture Albedo;
sampler AlbedoSampler : register(s0) = sampler_state { Texture = (Albedo); };

texture Backdrop;      // a copy of the render target under the drawable (advanced blend modes)
sampler BackdropSampler : register(s1) = sampler_state { Texture = (Backdrop); };

struct VertexIn
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;     // tint * opacity, opacity
    float4 Screen : COLOR1;    // screen tint, mask threshold
    float2 Uv : TEXCOORD0;
    float4 Region : TEXCOORD1; // the texture's area (min UV, max UV); it reads transparent outside
};

struct VertexOut
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float4 Screen : COLOR1;
    float2 Uv : TEXCOORD0;
    float4 Clip : TEXCOORD1;
    float4 Region : TEXCOORD2;
};

VertexOut MainVS(VertexIn input)
{
    VertexOut output;
    output.Position = mul(input.Position, Transform);
    output.Color = input.Color;
    output.Screen = input.Screen;
    output.Uv = input.Uv;
    output.Clip = output.Position;
    output.Region = input.Region;
    return output;
}

// The texel, transparent outside the texture's area, as with clamp-to-border and a transparent border (each texture is
// also cropped to its visible texels, which read transparent too).
float4 Texel(VertexOut input)
{
    float2 inside = step(input.Region.xy, input.Uv) * step(input.Uv, input.Region.zw);
    return tex2D(AlbedoSampler, input.Uv) * (inside.x * inside.y);
}

// A Part's colour: the screen tint on the premultiplied texel, then the tint and the opacity.
float4 PartColor(VertexOut input)
{
    float4 texel = Texel(input);
    float3 screened = 1 - (1 - texel.rgb) * (1 - input.Screen.rgb * texel.a);
    return float4(screened * input.Color.rgb, texel.a * input.Color.a);
}

float4 PartPS(VertexOut input) : COLOR0
{
    return PartColor(input);
}

// A mask source: it counts only where its alpha is above its threshold.
float4 MaskPS(VertexOut input) : COLOR0
{
    float4 texel = Texel(input);
    clip(texel.a > input.Screen.a ? 1 : -1);
    return float4(1, 1, 1, 1);
}

// KHR_blend_equation_advanced's f(Cs, Cd) per mode, on colours without premultiplication.
#define MULTIPLY 1
#define SCREEN 2
#define OVERLAY 3
#define DARKEN 4
#define LIGHTEN 5
#define COLORDODGE 6
#define COLORBURN 9
#define HARDLIGHT 10
#define SOFTLIGHT 11
#define DIFFERENCE 12
#define EXCLUSION 13

float3 HardLightOf(float3 s, float3 d)
{
    return s <= 0.5 ? 2 * s * d : 1 - 2 * (1 - s) * (1 - d);
}

float3 BlendColor(int mode, float3 s, float3 d)
{
    if (mode == MULTIPLY) return s * d;
    if (mode == SCREEN) return s + d - s * d;
    if (mode == OVERLAY) return HardLightOf(d, s);
    if (mode == DARKEN) return min(s, d);
    if (mode == LIGHTEN) return max(s, d);
    if (mode == COLORDODGE) return d <= 0 ? 0 : (s >= 1 ? 1 : min(1, d / max(1 - s, 1e-6)));
    if (mode == COLORBURN) return d >= 1 ? 1 : (s <= 0 ? 0 : 1 - min(1, (1 - d) / max(s, 1e-6)));
    if (mode == HARDLIGHT) return HardLightOf(s, d);
    if (mode == SOFTLIGHT)
    {
        float3 low = d - (1 - 2 * s) * d * (1 - d);
        float3 midDark = d + (2 * s - 1) * d * ((16 * d - 12) * d + 3);
        float3 midLight = d + (2 * s - 1) * (sqrt(d) - d);
        return s <= 0.5 ? low : (d <= 0.25 ? midDark : midLight);
    }
    if (mode == DIFFERENCE) return abs(d - s);
    return s + d - 2 * s * d; // EXCLUSION
}

// The drawable's colour blended with the backdrop as KHR_blend_equation_advanced does with premultiplied colours
// (X, Y and Z all 1). The result replaces the pixel (drawn with BlendState.Opaque).
float4 AdvancedColor(VertexOut input, int mode)
{
    float4 source = PartColor(input);
    float2 uv = (input.Clip.xy / input.Clip.w * float2(0.5, -0.5) + 0.5) * BackdropScale + PixelOffset;
    float4 backdrop = tex2D(BackdropSampler, uv);
    float3 s = source.a > 0 ? saturate(source.rgb / source.a) : 0;
    float3 d = backdrop.a > 0 ? saturate(backdrop.rgb / backdrop.a) : 0;
    float3 rgb = BlendColor(mode, s, d) * source.a * backdrop.a + source.rgb * (1 - backdrop.a) + backdrop.rgb * (1 - source.a);
    return float4(rgb, source.a + backdrop.a - source.a * backdrop.a);
}

float4 MultiplyPS(VertexOut input) : COLOR0 { return AdvancedColor(input, MULTIPLY); }
float4 ScreenPS(VertexOut input) : COLOR0 { return AdvancedColor(input, SCREEN); }
float4 OverlayPS(VertexOut input) : COLOR0 { return AdvancedColor(input, OVERLAY); }
float4 DarkenPS(VertexOut input) : COLOR0 { return AdvancedColor(input, DARKEN); }
float4 LightenPS(VertexOut input) : COLOR0 { return AdvancedColor(input, LIGHTEN); }
float4 ColorDodgePS(VertexOut input) : COLOR0 { return AdvancedColor(input, COLORDODGE); }
float4 ColorBurnPS(VertexOut input) : COLOR0 { return AdvancedColor(input, COLORBURN); }
float4 HardLightPS(VertexOut input) : COLOR0 { return AdvancedColor(input, HARDLIGHT); }
float4 SoftLightPS(VertexOut input) : COLOR0 { return AdvancedColor(input, SOFTLIGHT); }
float4 DifferencePS(VertexOut input) : COLOR0 { return AdvancedColor(input, DIFFERENCE); }
float4 ExclusionPS(VertexOut input) : COLOR0 { return AdvancedColor(input, EXCLUSION); }

// mgfxc defines SM4 for MonoGame's DirectX 11 backend, which needs shader model 4; FNA and MonoGame's OpenGL take 3.
#if SM4
#define TECHNIQUE(name, ps) technique name { pass { VertexShader = compile vs_4_0 MainVS(); PixelShader = compile ps_4_0 ps(); } }
#else
#define TECHNIQUE(name, ps) technique name { pass { VertexShader = compile vs_3_0 MainVS(); PixelShader = compile ps_3_0 ps(); } }
#endif

TECHNIQUE(Part, PartPS)
TECHNIQUE(Mask, MaskPS)
TECHNIQUE(Multiply, MultiplyPS)
TECHNIQUE(Screen, ScreenPS)
TECHNIQUE(Overlay, OverlayPS)
TECHNIQUE(Darken, DarkenPS)
TECHNIQUE(Lighten, LightenPS)
TECHNIQUE(ColorDodge, ColorDodgePS)
TECHNIQUE(ColorBurn, ColorBurnPS)
TECHNIQUE(HardLight, HardLightPS)
TECHNIQUE(SoftLight, SoftLightPS)
TECHNIQUE(Difference, DifferencePS)
TECHNIQUE(Exclusion, ExclusionPS)
