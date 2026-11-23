#version 410 core
precision highp float;

// Internal-HDR -> displayable SDR. The result is always written to an 8-bit target (or the
// window), so this is NOT an HDR output path. No monitor HDR capability is involved.

uniform sampler2D uSource;
uniform float uExposure;
in vec2 vUv;
out vec4 fragColor;

// ACES filmic approximation (Narkowicz), maps unbounded linear values into [0, 1].
vec3 ToneMap(vec3 x)
{
	const float a = 2.51;
	const float b = 0.03;
	const float c = 2.43;
	const float d = 0.59;
	const float e = 0.14;
	return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
	vec4 source = texture(uSource, vUv);
	fragColor = vec4(ToneMap(source.rgb * uExposure), source.a);
}
