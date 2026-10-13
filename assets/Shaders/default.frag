//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, Aditiya Afrizal, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#version 410 core
precision highp float;
in vec4 oViewPos;
in vec2 oUv;
in vec4 oColor;
in vec4 oLightResult;
// --- SHADOW MAPPING ---
uniform bool              uShadowEnabled;
uniform float             uShadowStrength;
uniform int               uShadowCascadeCount;
uniform bool              uShadowSmooth;
uniform float             uShadowFilterRadius;

uniform sampler2DShadow   uShadowMap0;
uniform sampler2DShadow   uShadowMap1;
uniform sampler2DShadow   uShadowMap2;
uniform sampler2DShadow   uShadowMap3;

uniform float             uShadowSplit0;      // Boundary where cascade 0 ends and 1 begins
uniform float             uShadowSplit1;      // Boundary where cascade 1 ends and 2 begins
uniform float             uShadowSplit2;      // Boundary where cascade 2 ends and 3 begins
uniform float             uShadowSplit3;      // Final shadow distance boundary

uniform float             uShadowBias0;
uniform float             uShadowBias1;
uniform float             uShadowBias2;
uniform float             uShadowBias3;

uniform float             uShadowNormalBias0;
uniform float             uShadowNormalBias1;
uniform float             uShadowNormalBias2;
uniform float             uShadowNormalBias3;

uniform vec2              uAlphaTest;
uniform sampler2D uTexture;

// --- PBR metallic-roughness (glTF and friends), GLSL 410 / macOS 4.1 safe ---
// BRDF after Filament (google/filament, Apache 2.0): GGX NDF + height-correlated
// Smith visibility (exact) + Schlick Fresnel. Nadrin/PBR (MIT, (c) Michal Siejak)
// as the original structural reference; split-sum env uses the Karis UE4
// analytic BRDF (no LUT texture) and a Filament quadratic LOD.
uniform float     uPBRMetallic;
uniform float     uPBRRoughness;
uniform vec3      uPBREmissive;
uniform sampler2D uNormalMap;
uniform sampler2D uOcclusionMap;
uniform sampler2D uEmissiveMap;
uniform sampler2D uMetallicRoughnessMap; // ARM: R occlusion, G roughness, B metallic
uniform sampler2D uSpecularMap; // KHR_materials_specular: specular factor in alpha
uniform sampler2D uSpecularColorMap; // KHR_materials_specular: F0 color in sRGB RGB
uniform int       uPBRMaps; // bit0 normal, bit1 occlusion, bit2 emissive, bit3 metallic-roughness, bit4 specular, bit5 specular-color
uniform float     uPBRNormalScale;
uniform float     uPBROcclusionStrength;
uniform float     uPBRSpecular; // KHR_materials_specular weight 0-1
uniform vec3      uPBRSpecularColor; // KHR_materials_specular F0 color, linear (may exceed 1)
uniform float     uPBRIor; // KHR_materials_ior (1.5 when unset, air outside at 1.0)
// Image-based lighting: one shipped equirect HDR, mipmaps stand in for a prefilter.
// uHasEnv 0 = analytic sun only; 1 = analytic + env (split-sum with analytic BRDF).
uniform sampler2D uEnvMap;
uniform float     uEnvMaxLod;
uniform int       uHasEnv;
uniform vec3      uCameraWorldPos;
uniform float     uPBRTransmission; // thin-glass factor 0-1 (KHR_materials_transmission approx)

struct Light
{
	vec3 position;
	vec3 ambient;
	vec3 diffuse;
	vec3 specular;
	vec4 lightModel;
};
uniform Light uLight;

// Inputs from vertex shader
in vec3  vNormal;
in vec3  vWorldPos;
in vec3  vWorldNormal;
in vec4  vPosLightSpace0;
in vec4  vPosLightSpace1;
in vec4  vPosLightSpace2;
in vec4  vPosLightSpace3;
uniform int uMaterialFlags;
uniform float uBrightness;
uniform float uOpacity;
uniform bool uIsFog;
uniform float uFogStart;
uniform float uFogEnd;
uniform vec3  uFogColor;
uniform float uFogDensity;
uniform bool uFogIsLinear;
out vec4 fragColor;

const float SHADOW_TWO_PI = 6.28318530718;
const float SHADOW_MIN_RADIUS = 0.5;
const float SHADOW_MAX_RADIUS = 3.0;
const float SHADOW_MIN_BLEND = 5.0;
const float SHADOW_MAX_BLEND = 25.0;
const float SHADOW_BLEND_FRACTION = 0.1;
const float SHADOW_BIAS_GUARD_TEXELS = 4.0;
const float SHADOW_BIAS_EPSILON = 0.00002;

// Interleaved Gradient Noise (Jimenez 2014) - per-pixel rotation without texture
float interleavedGradientNoise(vec2 p) {
    const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return fract(magic.z * fract(dot(p, magic.xy)));
}

// Precomputed Vogel disk (n=5): r=sqrt((i+0.5)/5), theta=i*GOLDEN_ANGLE.
// Saves 5x sqrt + 4x cos/sin per pixel vs computing per-tap; single
// rotation by phi (IGN) hides the sampling pattern.
const vec2 VOGEL_DISK_5[5] = vec2[5](
    vec2(0.31622777, 0.0),
    vec2(-0.40387357, 0.36998127),
    vec2(0.06181932, -0.70439930),
    vec2(0.50905647, 0.66397403),
    vec2(-0.93418124, -0.16524351)
);
vec2 vogelDiskSample(int i, float cosPhi, float sinPhi) {
    vec2 o = VOGEL_DISK_5[i];
    return vec2(o.x * cosPhi - o.y * sinPhi, o.x * sinPhi + o.y * cosPhi);
}

/// Samples a single cascade using hardware PCF.
/// When uShadowSmooth is true: 5-tap Vogel disk + IGN rotation (soft, no banding).
/// Each tap is hardware PCF bilinear (2x2) -> 5 taps effectively cover a smooth disk.
/// When false: 4-tap tight grid (0.5 texel) for sharp, pixel-perfect shadows.
/// bias is ~1 texel of depth (auto per-cascade + user). normalBias is now Unity-style
/// texels (typ. 0.3-1.0); slope acne is mostly handled by the vertex normal offset,
/// so the depth-side slope term stays small to avoid peter-panning.
float GetCascadeShadowFactor(sampler2DShadow shadowMap, vec4 posLightSpace, float bias, float normalBias)
{
    vec3 projCoords = posLightSpace.xyz / posLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;

    // Out-of-bounds check
    if (projCoords.x < 0.0 || projCoords.x > 1.0 ||
        projCoords.y < 0.0 || projCoords.y > 1.0 ||
        projCoords.z < 0.0 || projCoords.z > 1.0)
    {
        return 1.0;
    }

    // Slope-scaled Z-bias, deliberately small: 1 texel base + up to +1.5 texel at grazing.
    // Capped so a stale config can't push shadows off their caster.
    // Mesa-safe: a zero-length vNormal (uninitialized VAO normals) must not yield NaN.
    // Fall back to the light direction so the slope term is 0 and shadows stay attached.
    vec3 lightDir = uLight.position; // pre-normalized on CPU in SetLightPosition
    float nLen2 = dot(vNormal, vNormal);
    vec3 normal = nLen2 < 1e-10 ? lightDir : vNormal * inversesqrt(nLen2);
    float slope = clamp(1.0 - dot(normal, lightDir), 0.0, 1.0);
    float slopeScale = clamp(normalBias, 0.0, 1.5);
    float activeBias = bias * (1.0 + slope * slopeScale);

    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    // Single clamp, reused for both depth guard and UV spread.
    // Soft kernel spreads taps in UV; depth only needs a small guard, not full scaling.
    // Sharp (0.5 texel) -> 1.0x, max 3.0 -> ~1.6x (was ~2.9x, caused detachment in soft mode).
    float radiusScaled = uShadowSmooth ? clamp(uShadowFilterRadius, SHADOW_MIN_RADIUS, SHADOW_MAX_RADIUS) : SHADOW_MIN_RADIUS;
    float biasedDepth = projCoords.z - activeBias * (1.0 + (radiusScaled - SHADOW_MIN_RADIUS) * 0.25);
    // Hard clamp: never push more than ~4 texels of depth + epsilon.
    biasedDepth = max(biasedDepth, projCoords.z - (bias * SHADOW_BIAS_GUARD_TEXELS + SHADOW_BIAS_EPSILON));

    if (uShadowSmooth) {
        // Smooth path: Vogel disk + IGN - rotated per-pixel to hide sampling pattern.
        // radius in texels: 1.5 = soft but detailed (exposed via uShadowFilterRadius, tune 1.0-2.5).
        float phi = interleavedGradientNoise(gl_FragCoord.xy) * SHADOW_TWO_PI;
        float cosPhi = cos(phi);
        float sinPhi = sin(phi);
        float shadow = 0.0;
        // 5 taps, constant loop bounds -> driver will unroll; each tap is HW PCF bilinear.
        for (int i = 0; i < 5; ++i) {
            vec2 offset = vogelDiskSample(i, cosPhi, sinPhi) * texelSize * radiusScaled;
            shadow += texture(shadowMap, vec3(projCoords.xy + offset, biasedDepth));
        }
        shadow *= 0.2;
        return shadow;
    } else {
        // Sharp path: tight 4-tap grid at 0.5 texels, hardware PCF per tap.
        float shadow = 0.0;
        shadow += texture(shadowMap, vec3(projCoords.xy + vec2(-0.5, -0.5) * texelSize, biasedDepth));
        shadow += texture(shadowMap, vec3(projCoords.xy + vec2( 0.5, -0.5) * texelSize, biasedDepth));
        shadow += texture(shadowMap, vec3(projCoords.xy + vec2(-0.5,  0.5) * texelSize, biasedDepth));
        shadow += texture(shadowMap, vec3(projCoords.xy + vec2( 0.5,  0.5) * texelSize, biasedDepth));
        shadow *= 0.25;
        return shadow;
    }
}

/// Helper to sample a cascade by index.
float SampleCascadeByIndex(int idx)
{
    if (idx == 0) return GetCascadeShadowFactor(uShadowMap0, vPosLightSpace0, uShadowBias0, uShadowNormalBias0);
    if (idx == 1) return GetCascadeShadowFactor(uShadowMap1, vPosLightSpace1, uShadowBias1, uShadowNormalBias1);
    if (idx == 2) return GetCascadeShadowFactor(uShadowMap2, vPosLightSpace2, uShadowBias2, uShadowNormalBias2);
    if (idx == 3) return GetCascadeShadowFactor(uShadowMap3, vPosLightSpace3, uShadowBias3, uShadowNormalBias3);
    return 1.0;
}

/// Helper to get the split distance of a cascade by index.
float GetShadowSplitDistance(int idx)
{
    if (idx == 0) return uShadowSplit0;
    if (idx == 1) return uShadowSplit1;
    if (idx == 2) return uShadowSplit2;
    if (idx == 3) return uShadowSplit3;
    return 0.0;
}

/// Calculates the final shadow factor using CSM with smooth blending.
float CalculateShadowFactor()
{
    if (!uShadowEnabled) return 1.0;
    if (uShadowStrength <= 0.0) return 1.0; // strength 0 = no visible shadow, skip all fetches
    
    // Calculate view depth per-pixel for perspective correctness (crucial for large polygons like ground)
    float vViewDepth = abs(oViewPos.z);

    float shadow = 1.0;
    int cascadeCount = uShadowCascadeCount;

    for (int i = 0; i < cascadeCount; i++)
    {
        float splitDist = GetShadowSplitDistance(i);
        // Proportional blend: 10% of split distance, clamped. Fixed 15.0 was
        // too wide for near cascades and too narrow for far ones.
        float blendRange = clamp(splitDist * SHADOW_BLEND_FRACTION, SHADOW_MIN_BLEND, SHADOW_MAX_BLEND);

        if (vViewDepth < splitDist)
        {
            shadow = SampleCascadeByIndex(i);

            // Blend toward next cascade near the boundary
            if (i < cascadeCount - 1)
            {
                float blendStart = splitDist - blendRange;
                if (vViewDepth > blendStart)
                {
                    float nextShadow = SampleCascadeByIndex(i + 1);
                    float t = smoothstep(blendStart, splitDist, vViewDepth);
                    shadow = mix(shadow, nextShadow, t);
                }
            }
            else
            {
                // Last cascade: fade out at far edge
                float fadeStart = splitDist - blendRange * 2.0;
                if (vViewDepth > fadeStart)
                {
                    float t = smoothstep(fadeStart, splitDist, vViewDepth);
                    shadow = mix(shadow, 1.0, t);
                }
            }

            break;
        }
    }

    return mix(1.0, shadow, uShadowStrength);
}

const float PBR_PI = 3.14159265359;

// Real ACES (Stephen Hill RRT+ODT fit, same as three.js ACESFilmicToneMapping
// and the OCIO ACES output transform family): proper Academy transfer with
// sRGB in/out matrices, NOT the washed-out Narkowicz approximation.
// (Contrast "variants" live in AgX/Neutral, not ACES; three.js parity is what
//  matters here since web viewers are the reference.) Gamma encode follows.
vec3 PBR_ACES(vec3 x)
{
	const mat3 ACESInputMat = mat3(
		vec3(0.59719, 0.07600, 0.02840),
		vec3(0.35458, 0.90834, 0.13383),
		vec3(0.04823, 0.01566, 0.83777)
	);
	const mat3 ACESOutputMat = mat3(
		vec3(1.60475, -0.10208, -0.00327),
		vec3(-0.53108, 1.10813, -0.07276),
		vec3(-0.07367, -0.00605, 1.07602)
	);
	// three.js used a 1/0.6 viewing-environment tweak in its legacy-lights era
	// (see three.js #19621); current three.js and Blender apply no such
	// boost, and keeping it overexposes rims/highlights vs both. Exposure 1.
	vec3 c = x;
	c = ACESInputMat * c;
	vec3 a = c * (c + 0.0245786) - 0.000090537;
	vec3 b = c * (0.983729 * c + 0.4329510) + 0.238081;
	c = a / b;
	c = ACESOutputMat * c;
	return clamp(c, 0.0, 1.0);
}

// TBN-free normal mapping: Mikkelsen derivative frame, the same math as
// three.js perturbNormal2Arb (reference-viewer baseline). Magnitude is
// zoom-independent: the previous code fed the raw tangent slope where a
// per-pixel gradient belongs, so bumps inflated ~10x on close-ups (at 1:1
// texel:pixel it accidentally looked right).
vec3 PBRMappedNormal(vec3 surfPos, vec3 surfNorm, vec2 uv, float scale)
{
	vec3 mapN = texture(uNormalMap, uv).xyz * 2.0 - 1.0;
	vec3 N = normalize(surfNorm);
	if (!gl_FrontFacing) N = -N;
	vec3 q0 = dFdx(surfPos);
	vec3 q1 = dFdy(surfPos);
	vec2 st0 = dFdx(uv);
	vec2 st1 = dFdy(uv);
	vec3 q1perp = cross(q1, N);
	vec3 q0perp = cross(N, q0);
	vec3 T = q1perp * st0.x + q0perp * st1.x;
	vec3 B = q1perp * st0.y + q0perp * st1.y;
	float det = max(dot(T, T), dot(B, B));
	float tscale = det == 0.0 ? 0.0 : inversesqrt(det);
	return normalize(T * (mapN.x * scale * tscale) + B * (mapN.y * scale * tscale) + N * mapN.z);
}

// Filament standard model (google/filament, Apache 2.0): GGX NDF, height-
// correlated Smith visibility (exact form), Schlick Fresnel.
// `alpha` is roughness remapped: Filament Parameterization squares the
// perceptual (glTF) roughness ONCE, then D and V both take alpha.
float PBR_D_GGX(float NoH, float alpha)
{
	float a2 = alpha * alpha;
	float f = (NoH * a2 - NoH) * NoH + 1.0;
	return a2 / (PBR_PI * f * f);
}

float PBR_V_SmithGGX(float NoV, float NoL, float alpha)
{
	// Exact height-correlated form (Heitz 2014, Filament HIGH path).
	// The sqrt-free fast variant over-brightens at grazing and reads as
	// plastic under hot studio HDRs, so the two sqrt ALU is worth it.
	float a2 = alpha * alpha;
	float lambdaV = NoL * sqrt(NoV * NoV * (1.0 - a2) + a2);
	float lambdaL = NoV * sqrt(NoL * NoL * (1.0 - a2) + a2);
	return 0.5 / max(lambdaV + lambdaL, 1e-4);
}

// Split-sum scale/bias without a BRDF LUT (Karis 2013 UE4 approx, 4.1-safe).
// Takes PERCEPTUAL roughness + NoV, returns (scale, bias) so that
// envF = F0 * x + y matches Nadrin's (F0*DFG1+DFG2) without the texture.
vec2 PBR_EnvBRDF(float NoV, float perceptualRoughness)
{
	const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
	const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
	vec4 r = perceptualRoughness * c0 + c1;
	float a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
	return vec2(-1.04, 1.04) * a004 + r.zw;
}

vec3 PBR_F_Schlick(float cosTheta, vec3 F0, vec3 F90)
{
	return F0 + (F90 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Equirect direction -> UV. World is Y-up (glTF passes straight through):
// +Y (sky) maps to v=0, -Y (ground) to v=1, matching the RGBE layout.
vec2 PBR_EnvUV(vec3 d)
{
	float u = atan(d.z, d.x) / (2.0 * PBR_PI) + 0.5;
	float v = acos(clamp(d.y, -1.0, 1.0)) / PBR_PI;
	return vec2(u, v);
}

// albedo = baseColor x texture (finalColor.rgb at this point, sRGB); view-space math
// matching the existing uLight convention (position is a view-space direction).
// Linear workflow (approx gamma 2.2): web viewers render glTF in linear space with
// ACES tone mapping. We keep the shared sRGB framebuffer and convert at the edges.
vec3 PBRLighting(vec3 albedoSrgb, float shadow)
{
	vec3 albedo = pow(albedoSrgb, vec3(2.2));
	vec3 N = normalize(vNormal);
	if ((uPBRMaps & 1) != 0)
	{
		N = PBRMappedNormal(oViewPos.xyz, N, oUv, uPBRNormalScale);
	}
	vec3 V = normalize(-oViewPos.xyz);
	vec3 L = normalize(uLight.position);
	vec3 H = normalize(V + L);
	float roughness = clamp(uPBRRoughness, 0.04, 1.0);
	float metallic = clamp(uPBRMetallic, 0.0, 1.0);
	float armOcclusion = 1.0;
	if ((uPBRMaps & 8) != 0)
	{
		// glTF metallicRoughnessTexture: factors multiply the sampled channels
		vec3 arm = texture(uMetallicRoughnessMap, oUv).rgb;
		metallic *= arm.b;
		roughness *= arm.g;
		armOcclusion = arm.r;
		roughness = clamp(roughness, 0.04, 1.0);
		metallic = clamp(metallic, 0.0, 1.0);
	}
	vec3 F0;
	vec3 F90;
	{
		// KHR_materials_specular + KHR_materials_ior: F0 = min(f0_ior * specColor, 1) * spec.
		// f0_ior = ((ior - 1) / (ior + 1))^2 (outside air at 1.0; 1.5 gives 0.04).
		// ior 0 (spec-gloss conversion) yields f0 1, handing full control to specColor.
		float spec = clamp(uPBRSpecular, 0.0, 1.0);
		vec3 specColor = uPBRSpecularColor;
		if ((uPBRMaps & 32) != 0)
		{
			specColor *= pow(texture(uSpecularColorMap, oUv).rgb, vec3(2.2));
		}
		if ((uPBRMaps & 16) != 0)
		{
			spec *= texture(uSpecularMap, oUv).a;
			spec = clamp(spec, 0.0, 1.0);
		}
		float ior = max(uPBRIor, 0.0);
		float f0ior = ((ior - 1.0) / (ior + 1.0));
		f0ior *= f0ior;
		vec3 F0dielectric = min(f0ior * specColor, vec3(1.0)) * spec;
		// Metals ignore specular (spec §exclusions): F0 = albedo, F90 = 1 below.
		F0 = mix(F0dielectric, albedo, metallic);
		F90 = mix(vec3(spec), vec3(1.0), metallic);
	}
	// Filament Parameterization: perceptual (glTF) roughness squared once here;
	// D and V below both take alpha, the env LOD/BRDF below take perceptual.
	float perceptualRoughness = roughness;
	float alpha = roughness * roughness;
	float glassTrans = clamp(uPBRTransmission, 0.0, 1.0);
	float NdotL = max(dot(N, L), 0.0);
	float NdotV = max(dot(N, V), 0.0);
	float NDF = PBR_D_GGX(max(dot(N, H), 0.0), alpha);
	float Vis = PBR_V_SmithGGX(NdotV, NdotL, alpha);
	vec3 F = PBR_F_Schlick(max(dot(H, V), 0.0), F0, F90);
	vec3 kS = F;
	// Thin glass attenuates diffuse only (Filament thin-cubemap behavior):
	// specular/reflection stay full strength so glass keeps its sheen.
	// Base weight uses max(F) per KHR_materials_specular (directional energy).
	float kSmax = max(kS.r, max(kS.g, kS.b));
	vec3 kD = vec3(1.0 - kSmax) * (1.0 - metallic) * (1.0 - glassTrans);
	vec3 Lo = kD * albedo / PBR_PI * uLight.diffuse * NdotL;
	if (uHasEnv == 0)
	{
		// No IBL: the analytic sun is the only reflection source.
		// With IBL the environment owns reflections; keeping the sun's
		// micro-glints too doubles every sparkle into white glitter, so the
		// sun keeps diffuse (day/night, shadows) but not specular there.
		Lo += NDF * Vis * F * uLight.diffuse * NdotL;
	}
	float occlusion = armOcclusion;
	if ((uPBRMaps & 2) != 0)
	{
		occlusion = mix(1.0, texture(uOcclusionMap, oUv).r, clamp(uPBROcclusionStrength, 0.0, 1.0));
	}
	vec3 analyticAmbient = uLight.ambient * albedo * occlusion * (1.0 - metallic) * (1.0 - glassTrans);
	vec3 emissive = pow(uPBREmissive, vec3(2.2));
	if ((uPBRMaps & 4) != 0)
	{
		emissive *= pow(texture(uEmissiveMap, oUv).rgb, vec3(2.2));
	}
	if (uHasEnv != 0)
	{
		// IBL as reflections only: the scene sun keeps driving diffuse (so shadows
		// and day/night keep working on PBR), the environment adds speculars.
		// No env diffuse (that would double the sun), no analytic/env double sun:
		// metals are all-reflection, dielectrics add a tight sun glint on top.
		vec3 Nw = normalize(vWorldNormal);
		if ((uPBRMaps & 1) != 0)
		{
			// Same TBN-free mapping as the analytic path, but in world space:
			// the IBL reflection/refraction lobes must follow the bump detail.
			// The geometric normal alone smears the whole env into one sheet.
			Nw = PBRMappedNormal(vWorldPos, Nw, oUv, uPBRNormalScale);
		}
		// vWorldPos is camera-relative (the engine bakes -camera into the model
		// matrix), so the camera sits at the ORIGIN here: V = -P. Using the
		// absolute camera position instead offsets V by 2*Camera and bends the
		// reflections view-dependently (worst on the Z axis).
		vec3 Vw = normalize(-vWorldPos);
		vec3 Rw = reflect(-Vw, Nw);
		// Filament quadratic LOD: sharp at low roughness, blurrier mid-range.
		// Linear lod smeared the studio softbox and flattened the streaks.
		float envLod = uEnvMaxLod * perceptualRoughness * (2.0 - perceptualRoughness);
		vec3 radiance = textureLod(uEnvMap, PBR_EnvUV(Rw), envLod).rgb;
		// Split-sum without LUT: envF = F0 * scale + F90 * bias (Nadrin parity,
		// F90 = specular for dielectrics per KHR_materials_specular, 1 for metals).
		// Raw Schlick over-reflects at mid roughness, which was the other half
		// of the plastic look.
		float NoVw = max(dot(Nw, Vw), 0.0);
		vec2 envAB = PBR_EnvBRDF(NoVw, perceptualRoughness);
		vec3 Fenv = F0 * envAB.x + F90 * envAB.y;
		Lo += radiance * Fenv * occlusion;
		if (glassTrans > 0.001)
		{
			// Filament thin-glass cubemap path: transmitted env from the
			// refracted direction, weighted by what the reflection didn't take
			// ((1-E) * albedo * transmission). Alpha blending still handles the
			// actual see-through; this lobe tints the glass with the HDR so the
			// IBL reads on the lens instead of vanishing through the low alpha.
			vec3 Tr = refract(-Vw, Nw, 0.6667);
			if (dot(Tr, Tr) < 0.5) Tr = Rw;
			vec3 transRadiance = textureLod(uEnvMap, PBR_EnvUV(Tr), envLod).rgb;
			vec3 oneMinusE = clamp(vec3(1.0) - Fenv, vec3(0.0), vec3(1.0));
			Lo += transRadiance * oneMinusE * albedo * glassTrans;
		}
	}
	vec3 linear = (Lo + analyticAmbient) * shadow * uBrightness + emissive;
	return pow(clamp(PBR_ACES(linear), 0.0, 1.0), vec3(1.0 / 2.2));
}

void main(void)
{
	vec4 finalColor;
	if((uMaterialFlags & 16) == 0)
	{
		finalColor = vec4(oColor.rgb, 1.0) * texture(uTexture, oUv); // NOTE: only want the RGB of the color, A is passed in as part of opacity
	}
	else
	{
		// disable alpha channel when rendering texture (MSTS shape)
		finalColor = vec4(oColor.rgb, 1.0) * vec4(texture(uTexture, oUv).xyz, 1.0);
	}

	if((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0 && (uMaterialFlags & 128) == 0)
	{
		//Material is not emissive and lighting is enabled, so multiply by brightness
		//(PBR applies brightness itself inside PBRLighting - doing it here would darken twice)
		finalColor.rgb *= uBrightness;
	}
	
	// Multiply material alpha by it's opacity
	finalColor.a *= uOpacity;

	/*
	 * NOTES:
	 * Unused alpha functions must not be added to the shader
	 * This has a nasty affect on framerates
	 *
	 * A switch case block is also ~30% slower than the else-if
	 *
	 * Numbers used are those from the GL.AlphaFunction enum to allow
	 * for direct casts
	 */
	if(uAlphaTest.x == 513) // Less
	{
		if(finalColor.a >= uAlphaTest.y)
		{
			discard;
		}
	}
	else if(uAlphaTest.x == 514) // Equal
	{
		if(!(abs(finalColor.a - uAlphaTest.y) < 0.00001))
		{
			discard;
		}
	}
	else if(uAlphaTest.x == 516) // Greater
	{
		if(finalColor.a <= uAlphaTest.y)
		{
			discard;
		}
	}
		
	/*
	 * Apply the lighting results *after* the final color has been calculated
	 * This *must* also be done after the discard check to get correct results,
	 * as otherwise light coming through a semi-transparent material will 
	 * affect it's final opacity, and hence whether its discarded or not
	 */
	float shadow = CalculateShadowFactor();
	
	if ((uMaterialFlags & 128) != 0 && (uMaterialFlags & 4) == 0)
	{
		// PBR metallic-roughness path (file-agnostic: glTF, FBX, ...). Lighting is
		// computed per-fragment here; oLightResult (per-vertex Blinn-Phong) is ignored.
		finalColor.rgb = PBRLighting(finalColor.rgb, shadow);
	}
	else if ((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0)
	{
		// Material is not emissive, apply shadow to the light factor
		finalColor.rgb *= (oLightResult.rgb * shadow);
		finalColor.a *= oLightResult.a;
	}
	else
	{
		finalColor *= oLightResult;
	}
	
	// Fog
	float fogFactor = 1.0;

	if (uIsFog)
	{
		if(uFogIsLinear)
		{
			fogFactor = clamp((uFogEnd - length(oViewPos)) / (uFogEnd - uFogStart), 0.0, 1.0);
		}
		else
		{
			fogFactor = exp(-pow(uFogDensity * (gl_FragCoord.z / gl_FragCoord.w), 2.0));
		}
	}

	fragColor = vec4(mix(uFogColor, finalColor.rgb, fogFactor), finalColor.a);
}
