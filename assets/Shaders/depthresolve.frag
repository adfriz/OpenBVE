//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, Christopher Lees, S520, The OpenBVE Project
//
//Redistribution and use in source and binary forms, with or without
//modification, are permitted provided that the following conditions are met:
//
//1. Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//2. Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
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

/*
 * Resolves the multisampled scene depth into the single-sample opaque depth
 * texture used for occlusion discards in the peel composite and tail passes.
 *
 * glBlitFramebuffer cannot copy depth between framebuffers with different
 * multisample counts (GL_INVALID_OPERATION), so the depth is resolved here by
 * rendering a fullscreen quad into the opaque depth target and writing the
 * nearest sample's depth with gl_FragDepth.
 */

uniform sampler2DMS uSceneDepth;
uniform int uSampleCount;

void main()
{
	ivec2 coord = ivec2(gl_FragCoord.xy);
	float depth = texelFetch(uSceneDepth, coord, 0).r;
	for (int s = 1; s < uSampleCount; s++)
	{
		depth = min(depth, texelFetch(uSceneDepth, coord, s).r);
	}
	gl_FragDepth = depth;
}
