#version 410 core
precision highp float;

// Upsamples the half-resolution bokeh result over the sharp full-resolution source.
// The blur target's alpha carries per-pixel coverage, so in-focus pixels keep their
// original detail instead of being softened by the down/up-sample round trip.
// A 3x3 median post-filter (FidelityFX DoF) removes isolated bright specks left by
// the low tap count; coherent bokeh discs span several pixels and survive it.

uniform sampler2D uSource; // sharp, full resolution
uniform sampler2D uBlur;   // half resolution: rgb = blurred, a = coverage
uniform float uStrength;

in vec2 vUv;
out vec4 fragColor;

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

vec3 MedianBlur(vec2 uv)
{
	vec2 texel = 1.0 / vec2(textureSize(uBlur, 0));
	vec3 samples[9];
	int n = 0;
	for (int y = -1; y <= 1; y++)
	{
		for (int x = -1; x <= 1; x++)
		{
			samples[n] = texture(uBlur, uv + vec2(float(x), float(y)) * texel).rgb;
			n++;
		}
	}
	for (int i = 0; i < 9; i++)
	{
		for (int j = i + 1; j < 9; j++)
		{
			if (dot(samples[j], LUMA) < dot(samples[i], LUMA))
			{
				vec3 tmp = samples[i];
				samples[i] = samples[j];
				samples[j] = tmp;
			}
		}
	}
	return samples[4];
}

void main()
{
	vec4 sharp = texture(uSource, vUv);
	vec4 blur = texture(uBlur, vUv);
	vec3 median = MedianBlur(vUv);
	float luma = dot(blur.rgb, LUMA);
	vec3 filtered = blur.rgb * clamp(dot(median, LUMA) / max(luma, 0.0001), 0.0, 1.0);
	float amount = clamp(blur.a, 0.0, 1.0) * clamp(uStrength, 0.0, 1.0);
	fragColor = vec4(mix(sharp.rgb, filtered, amount), sharp.a);
}
