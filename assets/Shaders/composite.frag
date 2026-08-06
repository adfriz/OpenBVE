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
 * Fragment shader for the hybrid OIT fullscreen composite passes (drawn with the
 * renderer's dummy VAO as a 6-vertex triangle strip; see composite.vert).
 *
 * uMode:
 *   0 - Final composite: blends the resolved opaque scene (uOpaque) with the
 *       weighted-blended tail accumulation (uAccum / uReveal) per McGuire & Bavoil.
 *   1 - Peel composite: outputs the texture in uOpaque unchanged, preserving its
 *       alpha so the layer can be composited over the scene with standard OVER
 *       blending (SrcAlpha, OneMinusSrcAlpha).
 *   2 - Peel composite with opaque occlusion: as mode 1, but pixels where the
 *       peeled layer (uLayerDepth) lies BEHIND the resolved opaque scene depth
 *       (uOpaqueDepth) are discarded, so transparent fragments occluded by
 *       opaque geometry never reach the scene.
 */

in vec2 vUv;

uniform sampler2D uOpaque;
uniform sampler2D uAccum;
uniform sampler2D uReveal;
uniform sampler2D uLayerDepth;
uniform sampler2D uOpaqueDepth;
uniform int uMode;

out vec4 fragColor;

void main()
{
	vec4 opaque = texture(uOpaque, vUv);

	if (uMode == 1)
	{
		// Peel layer composite: keep the layer's own alpha for OVER blending
		fragColor = opaque;
		return;
	}

	if (uMode == 2)
	{
		// Peel layer composite with opaque occlusion discard: a peeled fragment
		// behind the opaque scene is invisible, so drop it.
		if (texture(uLayerDepth, vUv).r > texture(uOpaqueDepth, vUv).r)
		{
			discard;
		}
		fragColor = opaque;
		return;
	}

	vec3 accumColor = texture(uAccum, vUv).rgb;
	float accumAlpha = clamp(texture(uAccum, vUv).a, 1e-5, 1.0);
	float reveal = texture(uReveal, vUv).r;
	vec3 finalColor = accumColor / accumAlpha * (1.0 - reveal) + opaque.rgb * reveal;
	fragColor = vec4(finalColor, 1.0);
}
