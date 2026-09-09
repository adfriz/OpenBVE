#version 410 core
precision highp int;
precision highp float;

layout(location = 0) in vec3 iPosition;

out vec2 vUv;

void main()
{
	vUv = iPosition.xy * 0.5 + 0.5;
	gl_Position = vec4(iPosition.xy, 0.0, 1.0);
}
