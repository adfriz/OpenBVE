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
	vec4 src = texture(uInputTex, vUv);
	// intensity 0 = fallback copy, pixel-identical.
	if (uIntensity <= 0.001)
	{
		fragColor = src;
		return;
	}
	float k = clamp(uIntensity, 0.0, 2.0);
	vec2 d = vUv - vec2(0.5);
	// Aspect-correct distance so the vignette stays circular.
	d.x *= uResolution.x / max(uResolution.y, 1.0);
	float dist = length(d);
	// 1.0 at center, 0.0 at corners. Note: smoothstep needs edge0 < edge1.
	float vig = 1.0 - smoothstep(0.35, 0.85, dist);
	float dark = mix(1.0, vig, clamp(k, 0.0, 1.0));
	if (k > 1.0)
	{
		dark *= mix(1.0, vig, k - 1.0);
	}
	fragColor = vec4(src.rgb * dark, src.a);
}
