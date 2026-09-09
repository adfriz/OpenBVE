#version 410 core
precision highp int;
precision highp float;

in vec2 vUv;

uniform sampler2D uInputTex;
uniform vec2 uResolution;
uniform float uTime;
uniform float uIntensity;

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
	vec3 cNE = texture(uInputTex, vUv + vec2(texel.x, texel.y)).rgb;
	vec3 cNW = texture(uInputTex, vUv + vec2(-texel.x, texel.y)).rgb;
	vec3 cSE = texture(uInputTex, vUv + vec2(texel.x, -texel.y)).rgb;
	vec3 cSW = texture(uInputTex, vUv + vec2(-texel.x, -texel.y)).rgb;
	// Unsharp mask 3x3: sharpened = center + (center - blur) * k.
	vec3 blur = (cN + cS + cE + cW + cNE + cNW + cSE + cSW) * 0.125;
	float k = clamp(uIntensity, 0.0, 2.0);
	vec3 result = clamp(cC + (cC - blur) * k, 0.0, 1.0);
	fragColor = vec4(result, center.a);
}
