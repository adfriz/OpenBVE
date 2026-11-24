#version 410 core
precision highp float;

uniform sampler2D uSource;
uniform float uEncodeGamma; // 1 = encode linear SDR to display space as the very last step
in vec2 vUv;
out vec4 fragColor;

void main()
{
	vec4 color = texture(uSource, vUv);
	if (uEncodeGamma > 0.5)
	{
		color.rgb = pow(max(color.rgb, 0.0), vec3(1.0 / 2.2));
	}
	fragColor = color;
}
