// Shared color math for fragment post effects and AO kernels.
// A chunk is not a standalone shader: it carries no #version line and must be
// pulled in with #include "common_color.glsl" BELOW the consumer's own
// #version directive. Only basic GLSL is used so the chunk stays valid in both
// #version 410 core fragment shaders and 430 compute shaders.
// Consumers: post_fxaa, ao_smoke.

const vec3 LUMA_BT601 = vec3(0.299, 0.587, 0.114);

float Luma(vec3 c)
{
	return dot(c, LUMA_BT601);
}
