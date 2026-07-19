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
uniform sampler2D uTexture;
uniform vec2 uDirection; // (texelSize.x, 0) for horizontal, (0, texelSize.y) for vertical
uniform float uSpread;   // Multiplies the tap offset so the halo can be widened without extra passes.
out vec4 fragColor;

void main(void)
{
	// 9-tap separable Gaussian blur.
	float weights[5];
	weights[0] = 0.2270270270;
	weights[1] = 0.1945945946;
	weights[2] = 0.1216216216;
	weights[3] = 0.0540540541;
	weights[4] = 0.0162162162;

	vec2 spreadDir = uDirection * max(uSpread, 0.0);
	vec3 result = texture(uTexture, oUv).rgb * weights[0];

	result += texture(uTexture, oUv + spreadDir * 1.0).rgb * weights[1];
	result += texture(uTexture, oUv - spreadDir * 1.0).rgb * weights[1];
	result += texture(uTexture, oUv + spreadDir * 2.0).rgb * weights[2];
	result += texture(uTexture, oUv - spreadDir * 2.0).rgb * weights[2];
	result += texture(uTexture, oUv + spreadDir * 3.0).rgb * weights[3];
	result += texture(uTexture, oUv - spreadDir * 3.0).rgb * weights[3];
	result += texture(uTexture, oUv + spreadDir * 4.0).rgb * weights[4];
	result += texture(uTexture, oUv - spreadDir * 4.0).rgb * weights[4];

	fragColor = vec4(result, 1.0);
}
