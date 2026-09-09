#version 410 core
precision highp int;
precision highp float;

in vec2 vUv;

uniform sampler2D uInputTex;
uniform vec2 uResolution;
uniform float uTime;
uniform float uIntensity;

#include "common_color.glsl"

out vec4 fragColor;

void main()
{
	vec4 center = texture(uInputTex, vUv);
	// intensity 0 = fallback copy, pixel-identical.
	if (uIntensity <= 0.001)
	{
		fragColor = center;
		return;
	}
	vec2 texel = 1.0 / max(uResolution, vec2(1.0, 1.0));
	vec3 cC = center.rgb;
	vec3 cN = texture(uInputTex, vUv + vec2(0.0, texel.y)).rgb;
	vec3 cS = texture(uInputTex, vUv - vec2(0.0, texel.y)).rgb;
	vec3 cE = texture(uInputTex, vUv + vec2(texel.x, 0.0)).rgb;
	vec3 cW = texture(uInputTex, vUv - vec2(texel.x, 0.0)).rgb;
	float lC = Luma(cC);
	float lN = Luma(cN);
	float lS = Luma(cS);
	float lE = Luma(cE);
	float lW = Luma(cW);
	float lMin = min(lC, min(min(lN, lS), min(lE, lW)));
	float lMax = max(lC, max(max(lN, lS), max(lE, lW)));
	float contrast = lMax - lMin;
	// Flat areas: leave untouched to avoid blurring text/HUD edges.
	if (contrast < max(0.0312, 0.125 * lMax))
	{
		fragColor = center;
		return;
	}
	// Lightweight 5-tap cross resolve (~3.5-tap cost class): average the
	// cross, then blend 50/50 with center, gated by intensity.
	vec3 avg = (cN + cS + cE + cW) * 0.25;
	float k = clamp(uIntensity, 0.0, 1.0);
	vec3 result = mix(cC, avg * 0.5 + cC * 0.5, k);
	fragColor = vec4(result, center.a);
}
