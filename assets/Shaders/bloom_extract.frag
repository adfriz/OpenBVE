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
	float luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));

	// Soft-knee threshold: keep pixels above the threshold, smoothly ramp below it.
	float knee = max(uThreshold * 0.5, 0.0001);
	float soft = clamp((luminance - uThreshold + knee) / (2.0 * knee), 0.0, 1.0);
	soft = soft * soft;

	vec3 bloom = color.rgb * soft;

	// Hook for a future emission-mask buffer: raise emissive contribution.
	bloom += color.rgb * uEmissiveBoost * step(uThreshold, luminance);

	fragColor = vec4(bloom, 1.0);
}
