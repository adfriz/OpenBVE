#version 410 core
precision highp float;

// Internal-HDR -> displayable SDR. The result is always written to an 8-bit target (or the
// window), so this is NOT an HDR output path. No monitor HDR capability is involved.

uniform sampler2D uSource;
uniform float uExposure;
in vec2 vUv;
out vec4 fragColor;

// Same real ACES RRT+ODT fit as the PBR path. Gamma encoding is deliberately NOT
// applied here: the compositor applies it in the final blit, after every effect.
#include "tonemap/tonemap.glsl"

void main()
{
	vec4 source = texture(uSource, vUv);
	fragColor = vec4(PBR_ACES(source.rgb * uExposure), source.a);
}
