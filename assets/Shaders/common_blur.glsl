// Shared blur helpers for fragment post effects.
// Reserved foundation for future wide-blur passes (no consumer yet):
// TentBlur3x3 stays for small-radius glow; a separable Gaussian belongs here
// once a wide-radius consumer lands.
// A chunk is not a standalone shader: it carries no #version line and must be
// pulled in with #include "common_blur.glsl" BELOW the consumer's own
// #version directive. Only basic GLSL is used so chunks stay valid in both
// #version 410 core fragment shaders and 430 compute shaders.

vec3 TentBlur3x3(sampler2D tex, vec2 uv, vec2 texel)
{
	vec3 s = vec3(0.0);
	s += texture(tex, uv + vec2(-texel.x, texel.y)).rgb;
	s += texture(tex, uv + vec2(0.0, texel.y)).rgb;
	s += texture(tex, uv + vec2(texel.x, texel.y)).rgb;
	s += texture(tex, uv + vec2(-texel.x, 0.0)).rgb;
	s += texture(tex, uv).rgb;
	s += texture(tex, uv + vec2(texel.x, 0.0)).rgb;
	s += texture(tex, uv + vec2(-texel.x, -texel.y)).rgb;
	s += texture(tex, uv + vec2(0.0, -texel.y)).rgb;
	s += texture(tex, uv + vec2(texel.x, -texel.y)).rgb;
	return s * (1.0 / 9.0);
}
