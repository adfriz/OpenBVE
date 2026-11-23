#version 410 core
precision highp float;

uniform sampler2D uSource;
uniform float uStrength;
in vec2 vUv;
out vec4 fragColor;

void main()
{
	vec4 source = texture(uSource, vUv);
	float distanceFromCentre = length(vUv - 0.5) * 1.41421356;
	float darkening = 1.0 - smoothstep(0.25, 0.75, distanceFromCentre) * uStrength;
	fragColor = vec4(source.rgb * darkening, source.a);
}
