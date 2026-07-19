//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the above disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the above disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#version 410 core
precision highp float;

in vec2 oUv;
uniform sampler2D uScene;
uniform sampler2D uDepth;
uniform mat4 uCurrentViewProj;
uniform mat4 uPreviousViewProj;
uniform mat4 uInverseViewProj;
uniform int uSamples;
uniform float uStrength;
out vec4 fragColor;

const int MAX_SAMPLES = 16;

vec3 reconstructWorld(vec2 uv, float depth)
{
	// Depth buffer stores window-space depth in [0,1]; convert to NDC z in [-1,1].
	// Inverse view-projection is precomputed on the CPU (uInverseViewProj) to avoid
	// a per-pixel matrix inverse in this shader.
	vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
	vec4 world = uInverseViewProj * clip;
	world /= world.w;
	return world.xyz;
}

void main(void)
{
	float depth = texture(uDepth, oUv).r;

	// Sky / far plane (no depth): nothing to blur.
	// Also guard against an invalid/unpopulated depth texture (depth == 0),
	// which would otherwise reproject to garbage UVs and sample off-screen.
	if (depth >= 1.0 || depth <= 0.0)
	{
		fragColor = vec4(texture(uScene, oUv).rgb, 1.0);
		return;
	}

	vec3 world = reconstructWorld(oUv, depth);

	// Reproject into the previous frame to obtain the per-pixel screen velocity.
	vec4 prevClip = uPreviousViewProj * vec4(world, 1.0);
	prevClip /= prevClip.w;
	vec2 prevUv = prevClip.xy * 0.5 + 0.5;
	vec2 velocity = (oUv - prevUv) * uStrength;

	int samples = uSamples;
	vec3 result = texture(uScene, oUv).rgb;
	vec2 step = velocity / float(samples);
	for (int i = 1; i < MAX_SAMPLES; i++)
	{
		if (i >= samples)
		{
			break;
		}
		vec2 uv = oUv - velocity * (float(i) / float(samples));
		result += texture(uScene, uv).rgb;
	}
	result /= float(samples);

	fragColor = vec4(result, 1.0);
}
