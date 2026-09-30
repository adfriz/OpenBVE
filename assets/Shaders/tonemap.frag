// Copy for now. Real curves (Reinhard, ACES) drop in here later.

#version 410 core
precision highp float;
uniform sampler2D uHdrBuffer;
uniform float uExposure;
in vec2 oUv;
out vec4 fragColor;
void main()
{
	vec3 c = texture(uHdrBuffer, oUv).rgb * uExposure;
	fragColor = vec4(c, 1.0);
}
