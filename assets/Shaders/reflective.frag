// Simplified BSD License (BSD-2-Clause)
// Copyright (c) 2024, Aditiya Afrizal, The OpenBVE Project

#version 410 core
precision highp float;

in vec2  vUv;
in float vDepth;

uniform sampler2D uTexture;
uniform bool      uHasTexture;
// Discard pixels outside the active hemisphere (vDepth > 1 means behind the paraboloid).
// Also discards fragments further than the probe's far plane.

out vec4 fragColor;

void main()
{
    // Reject fragments that lie behind or beyond the capture hemisphere.
    if (vDepth < 0.0 || vDepth > 1.0)
        discard;

    if (uHasTexture)
    {
        fragColor = texture(uTexture, vUv);
        // Discard fully transparent texels to keep the probe clean.
        if (fragColor.a < 0.01)
            discard;
    }
    else
    {
        fragColor = vec4(0.5, 0.5, 0.5, 1.0);
    }
}
