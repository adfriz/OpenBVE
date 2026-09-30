#version 410 core
layout(location = 0) in vec2 iPosition;
layout(location = 1) in vec2 iUv;
out vec2 oUv;
void main()
{
	oUv = iUv;
	gl_Position = vec4(iPosition, 0.0, 1.0);
}
