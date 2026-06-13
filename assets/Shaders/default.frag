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

uniform bool uIsPbr;
uniform sampler2D uNormalMap;
uniform sampler2D uOrmMap;
uniform float uMetallicConstant;
uniform float uRoughnessConstant;
uniform bool uHasNormalMap;
uniform bool uHasOrmMap;

const float PI = 3.14159265359;

// Screen-space TBN matrix generation for normal maps
vec3 getNormalFromMap()
{
    vec3 tangentNormal = texture(uNormalMap, oUv).xyz * 2.0 - 1.0;

    vec3 Q1  = dFdx(oViewPos.xyz);
    vec3 Q2  = dFdy(oViewPos.xyz);
    vec2 st1 = dFdx(oUv);
    vec2 st2 = dFdy(oUv);

    vec3 N   = normalize(vNormal);
    vec3 T  = normalize(Q1*st2.t - Q2*st1.t);
    vec3 B  = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);

    return normalize(TBN * tangentNormal);
}

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

float DistributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness*roughness;
    float a2 = a*a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    
    float num = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    return num / (PI * denom * denom);
}

float GeometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 calculatePBR(vec3 albedo, vec3 N, vec3 V, vec3 L, float roughness, float metallic, float ao, float shadow)
{
    vec3 F0 = vec3(0.04); 
    F0 = mix(F0, albedo, metallic);

    vec3 H = normalize(V + L);

    float NDF = DistributionGGX(N, H, roughness);   
    float G   = GeometrySmith(N, V, L, roughness);      
    vec3 F    = fresnelSchlick(max(dot(H, V), 0.0), F0);
           
    vec3 numerator    = NDF * G * F; 
    float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001;
    vec3 specular = numerator / denominator;
    
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;	  

    float NdotL = max(dot(N, L), 0.0);
    vec3 radiance = uLight.diffuse;

    vec3 Lo = (kD * albedo / PI + specular) * radiance * NdotL * shadow;
    vec3 ambient = uLight.ambient * albedo * ao;

    vec3 color = ambient + Lo;
    return color;
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
	
	if (uIsPbr)
	{
		vec3 albedo = finalColor.rgb;
		vec3 N = uHasNormalMap ? getNormalFromMap() : normalize(vNormal);
		vec3 V = normalize(-oViewPos.xyz);
		vec3 L = normalize(uLight.position);
		
		float roughness = uRoughnessConstant;
		float metallic = uMetallicConstant;
		float ao = 1.0;
		if (uHasOrmMap)
		{
			vec3 orm = texture(uOrmMap, oUv).rgb;
			ao = orm.r;
			roughness = orm.g;
			metallic = orm.b;
		}
		
		vec3 pbrColor = calculatePBR(albedo, N, V, L, roughness, metallic, ao, shadow);
		finalColor.rgb = pbrColor;
	}
	else
	{
		if ((uMaterialFlags & 1) == 0 && (uMaterialFlags & 4) == 0)
		{
			// Material is not emissive, apply shadow to the light factor
			finalColor.rgb *= (oLightResult.rgb * shadow);
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
