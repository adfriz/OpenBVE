// HDR to screen. Mode 0 = copy (legacy look), 1 = Reinhard, 2 = ACES. sRGB only when tonemapping.

#version 410 core
precision highp float;
uniform sampler2D uHdrBuffer;
uniform float uExposure;
uniform int uMode;
in vec2 oUv;
out vec4 fragColor;

vec3 Reinhard(vec3 c)
{
	return c / (1.0 + c);
}

// Narkowicz ACES fit, good enough for games.
vec3 Aces(vec3 x)
{
	const float a = 2.51;
	const float b = 0.03;
	const float c = 2.43;
	const float d = 0.59;
	const float e = 0.14;
	return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

vec3 LinearToSrgb(vec3 c)
{
	c = max(c, vec3(0.0));
	vec3 lo = 12.92 * c;
	vec3 hi = 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055;
	return mix(lo, hi, step(vec3(0.0031308), c));
}

void main()
{
	vec3 c = texture(uHdrBuffer, oUv).rgb * uExposure;
	if (uMode == 1)
	{
		c = Reinhard(c);
	}
	else if (uMode == 2)
	{
		c = Aces(c);
	}
	if (uMode != 0)
	{
		c = LinearToSrgb(c);
	}
	fragColor = vec4(c, 1.0);
}
