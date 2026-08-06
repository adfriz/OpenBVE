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
 * REFERENCE fragment shader for the hybrid OIT tail pass.
 *
 * The production tail pass does NOT use this file: the weighted-blended output is
 * produced by compiling the regular default.vert + default.frag sources into a
 * second Shader instance and setting the uOitMode uniform to 1 (see
 * LibRender2.OIT.OitRenderer). This guarantees pixel-identical shading with the
 * regular render path (lighting, fog, alpha test, texture matrices).
 *
 * This file documents the output contract of the tail shader and is a minimal,
 * valid standalone implementation of the same contract:
 *
 *   location 0 -> uAccum  = vec4(color.rgb * alpha, alpha) * weight
 *   location 1 -> uReveal = vec4(alpha)
 *
 * with the depth-based weight from McGuire & Bavoil, "Weighted Blended
 * Order-Independent Transparency", JCGT Vol. 2, No. 2, 2013.
 */

in vec4 oViewPos;
in vec2 oUv;
in vec4 oColor;

uniform sampler2D uTexture;

layout(location = 0) out vec4 oitAccum;
layout(location = 1) out vec4 oitReveal;

void main()
{
	vec4 color = vec4(oColor.rgb, 1.0) * texture(uTexture, oUv);
	float alpha = color.a;
	float viewZ = abs(oViewPos.z);
	float weight = alpha * clamp(0.03 / (1e-5 + pow(viewZ / 200.0, 4.0)), 0.01, 3000.0);
	oitAccum = vec4(color.rgb * alpha, alpha) * weight;
	oitReveal = vec4(alpha);
}
