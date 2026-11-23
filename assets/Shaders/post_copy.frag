#version 410 core
precision highp float;

uniform sampler2D uSource;
in vec2 vUv;
out vec4 fragColor;

void main()
{
	fragColor = texture(uSource, vUv);
}
