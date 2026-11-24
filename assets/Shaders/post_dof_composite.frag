#version 410 core
precision highp float;

// Upsamples the half-resolution bokeh result over the sharp full-resolution source.
// The blur target's alpha carries per-pixel coverage, so in-focus pixels keep their
// original detail instead of being softened by the down/up-sample round trip.

uniform sampler2D uSource; // sharp, full resolution
uniform sampler2D uBlur;   // half resolution: rgb = blurred, a = coverage
uniform float uStrength;

in vec2 vUv;
out vec4 fragColor;

void main()
{
	vec4 sharp = texture(uSource, vUv);
	vec4 blur = texture(uBlur, vUv);
	float amount = clamp(blur.a, 0.0, 1.0) * clamp(uStrength, 0.0, 1.0);
	fragColor = vec4(mix(sharp.rgb, blur.rgb, amount), sharp.a);
}
