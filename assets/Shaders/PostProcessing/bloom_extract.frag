//Simplified BSD License (BSD-2-Clause)
//
//Copyright (c) 2024, The OpenBVE Project
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

in vec2 oUv;
uniform sampler2D uScene;
uniform float uThreshold;
uniform float uEmissiveBoost;
out vec4 fragColor;

void main(void)
{
	vec4 color = texture(uScene, oUv);

	// Selective bloom: the alpha channel of the scene buffer is the emissive mask
	// written by the material shader (0 for non-emissive, emissive brightness for
	// emissive geometry). Only pixels with a non-zero mask contribute to the bloom,
	// so bright non-emissive backgrounds never glow regardless of their luminance.
	float mask = color.a;

	// Hard cutoff at uThreshold so non-emissive pixels (mask == 0) never bloom.
	float soft = step(uThreshold, mask);

	vec3 bloom = color.rgb * mask * soft;

	// Optional extra boost for emissive pixels.
	bloom += color.rgb * uEmissiveBoost * soft;

	fragColor = vec4(bloom, 1.0);
}
