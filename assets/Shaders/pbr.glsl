//Metallic-roughness PBR core for default.frag. Included via `#include "pbr.glsl"`,
//which AbstractShader resolves (embedded resource or file, relative to the parent).
//Requires tonemap.glsl to be included first: PBRLighting ends in PBR_Output.
//BRDF after Filament (google/filament, Apache 2.0): GGX NDF + height-correlated
//Smith visibility (exact) + Schlick Fresnel. Nadrin/PBR (MIT, (c) Michal Siejak)
//as the original structural reference; split-sum env uses the Karis UE4
//analytic BRDF (no LUT texture) and a Filament quadratic LOD.
//
//Requires from the includer: the uPBR* / uEnv* uniforms, uLight, vNormal,
//vWorldPos, vWorldNormal, oUv, oViewPos, uBrightness.

const float PBR_PI = 3.14159265359;

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
	return PBR_Output(linear);
}