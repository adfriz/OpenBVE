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

struct Light
{
	vec3 position;
	vec3 ambient;
	vec3 diffuse;
	vec3 specular;
	vec4 lightModel;
};
uniform Light uLight;

struct DynamicLight {
	int type;
	vec3 position;
	vec3 direction;
	vec4 color;
	float range;
	float rangeSquared;
	float spotCutoff;
	float power;
	float exposure;
	int isNormalize;
	float radius;
	int softFalloff;
	float softness;
};
uniform int uDynamicLightCount;
uniform DynamicLight uDynamicLights[16];

// --- CLUSTERED FORWARD (410-compatible UBO, no SSBO) ---
// PBR_BIT = 128: free bit. Used bits are 1=Emissive, 2=TransparentColor,
// 4=DisableLighting, 8=CrossFadeTexture, 16=DisableTextureAlpha(MSTS),
// 32=NoShadow, 64=Specular. So 32/64 are NOT free; 128 is the next free bit.
#define PBR_BIT 128

// Single uniform block for 410 path. Size audit (std140):
//  uClusterLightPosRange[64]        : 64*16 = 1024 B
//  uClusterLightColorIntensity[64]  : 64*16 = 1024 B
//  uClusterLightDirCutoff[64]       : 64*16 = 1024 B  (lights subtotal 3072 B)
//  uClusterHeaders[128] (ivec2)     : 128*16 = 2048 B (offset,count per tile)
//  uClusterIndices[2048] (int)      : 2048*16 = 32768 B (std140 int stride = 16 B)
//  TOTAL = 3072 + 2048 + 32768 = 37888 B < 65536 B (64KB) per-block limit. OK.
// Bound via GL.UniformBlockBinding(uClusterData -> binding 1) in Shader.cs
// (binding 0 is reserved for uAnimationMatricies). No `binding=` qualifier
// in GLSL to stay 410-compatible (bind points set from C#).
layout(std140) uniform uClusterData
{
	vec4	uClusterLightPosRange[64];		// xyz = view-space pos, w = range
	vec4	uClusterLightColorIntensity[64];	// rgb = color, w = intensity (power*exp2(exposure)/norm)
	vec4	uClusterLightDirCutoff[64];		// xyz = view-space dir, w = outer spot cutoff
	ivec2	uClusterHeaders[128];			// x = offset into indices, y = count
	int	uClusterIndices[2048];			// light indices (max 64)
};

uniform int	uClusteringEnabled;	// 0 = legacy uDynamicLights[16] path, !=0 = clustered path
uniform float	uClusterScreenWidth;
uniform float	uClusterScreenHeight;
uniform int	uClusterNumX;
uniform int	uClusterNumY;
uniform int	uClusterNumZ;	// compat (3D grid depth, unused by 2D tile lookup)
uniform float	uClusterNear;	// compat
uniform float	uClusterFar;	// compat
uniform float	uTileSize;	// tile size in pixels (square tiles); fallback = screen/num if <= 0

int getTileIdx(vec4 fragCoord)
{
	float tileW = (uTileSize > 0.5) ? uTileSize : (uClusterScreenWidth / max(float(max(uClusterNumX, 1)), 1.0));
	float tileH = (uTileSize > 0.5) ? uTileSize : (uClusterScreenHeight / max(float(max(uClusterNumY, 1)), 1.0));
	int tx = int(floor(fragCoord.x / max(tileW, 1.0)));
	int ty = int(floor(fragCoord.y / max(tileH, 1.0)));
	tx = clamp(tx, 0, max(uClusterNumX - 1, 0));
	ty = clamp(ty, 0, max(uClusterNumY - 1, 0));
	return tx + ty * max(uClusterNumX, 1);
}

// --- Cook-Torrance GGX helpers (PBR path, flag-gated by PBR_BIT) ---
float D_GGX(float NdotH, float roughness)
{
	float a = roughness * roughness;
	float a2 = a * a;
	float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
	return a2 / (3.14159265 * denom * denom + 0.0001);
}
float V_SmithGGX(float NdotV, float NdotL, float roughness)
{
	float r = roughness + 1.0;
	float k = (r * r) / 8.0;
	float G1V = NdotV / (NdotV * (1.0 - k) + k + 0.0001);
	float G1L = NdotL / (NdotL * (1.0 - k) + k + 0.0001);
	float G = G1V * G1L;
	return G / (4.0 * max(NdotV * NdotL, 0.0001));
}
vec3 F_Schlick(vec3 F0, float VdotH)
{
	return F0 + (vec3(1.0) - F0) * pow(clamp(1.0 - VdotH, 0.0, 1.0), 5.0);
}

// Inputs from vertex shader
in vec3  vNormal;
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

/// Samples a single cascade using hardware PCF.
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

    // Compute slope-scaled Z-bias dynamically based on the exact texel size fraction passed from C#.
    vec3 normal = normalize(vNormal);
    vec3 lightDir = normalize(uLight.position);
    float biasScale = clamp(1.0 - dot(normal, lightDir), 0.0, 1.0);
    // Multiply the base Z-bias by a slope factor to perfectly cure acne on thin meshes
    float activeBias = bias * (1.0 + biasScale * normalBias); 

    float currentDepth = projCoords.z - activeBias;

    // Tight 4-tap rotated grid PCF for sharper shadows.
    // Each tap is bilinear-averaged by the hardware sampler2DShadow.
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    float shadow = 0.0;
    
    // Rotated grid offsets at a tight 0.5 texels
    shadow += texture(shadowMap, vec3(projCoords.xy + vec2(-0.5, -0.5) * texelSize, currentDepth));
    shadow += texture(shadowMap, vec3(projCoords.xy + vec2( 0.5, -0.5) * texelSize, currentDepth));
    shadow += texture(shadowMap, vec3(projCoords.xy + vec2(-0.5,  0.5) * texelSize, currentDepth));
    shadow += texture(shadowMap, vec3(projCoords.xy + vec2( 0.5,  0.5) * texelSize, currentDepth));
    shadow *= 0.25;

    return shadow;
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
    
    // Calculate view depth per-pixel for perspective correctness (crucial for large polygons like ground)
    float vViewDepth = abs(oViewPos.z);

    float blendRange = 15.0;
    float shadow = 1.0;
    int cascadeCount = uShadowCascadeCount;

    for (int i = 0; i < cascadeCount; i++)
    {
        float splitDist = GetShadowSplitDistance(i);

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
                    float t = (vViewDepth - blendStart) / blendRange;
                    shadow = mix(shadow, nextShadow, t);
                }
            }
            else
            {
                // Last cascade: fade out at far edge
                float fadeStart = splitDist - blendRange * 2.0;
                if (vViewDepth > fadeStart)
                {
                    float t = (vViewDepth - fadeStart) / (splitDist - fadeStart);
                    shadow = mix(shadow, 1.0, t);
                }
            }

            break;
        }
    }

    return mix(1.0, shadow, uShadowStrength);
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

	if((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0)
	{
		//Material is not emissive and lighting is enabled, so multiply by brightness
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
	
	vec3 dynamicLightSum = vec3(0.0);
	if ((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0 && uDynamicLightCount > 0)
	{
		// Precalculated constants for faster lighting calculations
		const float ONE_OVER_FOUR_PI = 0.0795774715; // 1.0 / (4.0 * PI)
		const float TWO_PI = 6.283185307;           // 2.0 * PI
		vec3 N = normalize(vNormal);
		if (uClusteringEnabled != 0)
		{
			// --- Clustered path: tile lookup -> header -> up to 16 indices ---
			// Same attenuation/spot formulas as legacy below (radius=0, no softFalloff,
			// spot-vs-point inferred from cutoff, intensity pre-normalized on CPU).
			int tileIdx = clamp(getTileIdx(gl_FragCoord), 0, 127);
			ivec2 hdr = uClusterHeaders[tileIdx];
			int coff = hdr.x;
			int ccnt = hdr.y;
			int ncl = min(max(ccnt, 0), 16);
			for (int k = 0; k < 16; k++)
			{
				if (k >= ncl) break;
				int li = uClusterIndices[clamp(coff + k, 0, 2047)];
				li = clamp(li, 0, 63);
				vec3 cPos = uClusterLightPosRange[li].xyz;
				float cRange = uClusterLightPosRange[li].w;
				vec3 cCol = uClusterLightColorIntensity[li].rgb;
				float cInt = uClusterLightColorIntensity[li].w;
				vec3 cDir = uClusterLightDirCutoff[li].xyz;
				float cCut = uClusterLightDirCutoff[li].w;
				vec3 toLight = cPos - oViewPos.xyz;
				float d2 = dot(toLight, toLight);
				float cRangeSq = cRange * cRange;
				if (d2 <= cRangeSq)
				{
					float d = sqrt(d2);
					vec3 L = toLight / max(d, 0.0001);

					vec3 lightColor = cCol * cInt;

					float denom = d2; // radius = 0 in compact cluster format
					float att = 1.0 / max(denom, 0.0001);

					vec3 lightToFrag = -L;
					float spotDot = dot(lightToFrag, cDir);
					float outerCutoff = cCut;

					float softnessFactor = 0.0;
					float innerCutoff = mix(1.0, outerCutoff, 1.0 - softnessFactor);

					float intensityFactor = clamp((spotDot - outerCutoff) / max(innerCutoff - outerCutoff, 0.0001), 0.0, 1.0);
					float spotAtt = smoothstep(0.0, 1.0, intensityFactor) * step(outerCutoff, spotDot);

					float isSpot = (outerCutoff < 0.999) ? 1.0 : 0.0;
					att *= mix(1.0, spotAtt, isSpot);

					float nDotL = abs(dot(N, L));
					dynamicLightSum += lightColor * nDotL * att;
				}
			}
		}
		else
		{
		for (int i = 0; i < uDynamicLightCount; i++)
		{
			vec3 toLight = uDynamicLights[i].position - oViewPos.xyz;
			float d2 = dot(toLight, toLight);
			if (d2 <= uDynamicLights[i].rangeSquared)
			{
				float d = sqrt(d2);
				vec3 L = toLight / d;
				
				// 1. Calculate Intensity: Power in Watts, Exposure, Normalize
				float intensity = uDynamicLights[i].power * exp2(uDynamicLights[i].exposure);
				float solidAngle = TWO_PI * (1.0 - uDynamicLights[i].spotCutoff);
				bool normalizeSpot = (uDynamicLights[i].type == 1 && uDynamicLights[i].isNormalize != 0);
				intensity /= normalizeSpot ? max(solidAngle, 0.0001) : 12.566370614;
				
				vec3 lightColor = uDynamicLights[i].color.rgb * intensity;

				// 2. Attenuation: SoftFalloff, Radius
				float denom = d2 + uDynamicLights[i].radius * uDynamicLights[i].radius;
				float att = 1.0 / max(denom, 0.0001);
				if (uDynamicLights[i].softFalloff != 0)
				{
					att *= clamp((uDynamicLights[i].range - d) / max(0.001, uDynamicLights[i].range * 0.2), 0.0, 1.0);
				}

				// 3. Spot Cone Attenuation (Branchless)
				vec3 lightToFrag = -L;
				float spotDot = dot(lightToFrag, uDynamicLights[i].direction);
				float outerCutoff = uDynamicLights[i].spotCutoff;
				
				float softnessFactor = clamp(uDynamicLights[i].softness, 0.0, 1.0);
				float innerCutoff = mix(1.0, outerCutoff, 1.0 - softnessFactor);
				
				float intensityFactor = clamp((spotDot - outerCutoff) / max(innerCutoff - outerCutoff, 0.0001), 0.0, 1.0);
				float spotAtt = smoothstep(0.0, 1.0, intensityFactor) * step(outerCutoff, spotDot);
				
				att *= mix(1.0, spotAtt, float(uDynamicLights[i].type == 1));

				float nDotL = abs(dot(N, L));
				dynamicLightSum += lightColor * nDotL * att;
			}
		}
		} // end else (legacy fallback)
	}

	if ((uMaterialFlags & PBR_BIT) != 0)
	{
		// --- PBR path (Cook-Torrance GGX, flag-gated) ---
		// metallic/roughness hardcoded for now (met=0, rough=0.5); structure compiles,
		// legacy path below stays byte-identical when flag == 0.
		if ((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0)
		{
			vec3 albedo = finalColor.rgb;
			float metallic = 0.0;
			float roughness = 0.5;
			vec3 Npbr = normalize(vNormal);
			vec3 Vpbr = normalize(-oViewPos.xyz + vec3(0.0, 0.0, 0.0001));
			vec3 F0 = mix(vec3(0.04), albedo, metallic);
			vec3 Lpbr = normalize(uLight.position);
			vec3 Hpbr = normalize(Vpbr + Lpbr);
			float NdotL = abs(dot(Npbr, Lpbr));
			float NdotV = abs(dot(Npbr, Vpbr)) + 0.0001;
			float NdotH = max(dot(Npbr, Hpbr), 0.0);
			float VdotH = max(dot(Vpbr, Hpbr), 0.0);
			float D = D_GGX(NdotH, roughness);
			float Vv = V_SmithGGX(NdotV, NdotL, roughness);
			vec3 F = F_Schlick(F0, VdotH);
			vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
			vec3 diffuse = kD * albedo / 3.14159265;
			vec3 spec = D * Vv * F;
			vec3 totalLightPbr = oLightResult.rgb * shadow + dynamicLightSum;
			finalColor.rgb = (diffuse + spec) * totalLightPbr;
			finalColor.a *= oLightResult.a;
		}
		else
		{
			finalColor *= oLightResult;
		}
	}
	else
	{
	if ((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0)
	{
		// Material is not emissive, apply shadow to the light factor and add dynamic lights
		vec3 totalLight = oLightResult.rgb * shadow + dynamicLightSum;
		finalColor.rgb *= totalLight;
		finalColor.a *= oLightResult.a;
	}
	else
	{
		finalColor *= oLightResult;
	}
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
