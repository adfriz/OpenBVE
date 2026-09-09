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
	// Passthrough: pixel-identical copy.
	// uResolution / uTime / uIntensity are reserved for the shared
	// post-effect interface and intentionally unused here so OFF/identity
	// output never alters the image.
	vec4 c = texture(uInputTex, vUv);
	fragColor = c;
}
