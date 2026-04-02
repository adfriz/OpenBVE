#version 430 core

in vec2 vUv;

uniform sampler2D uTexture;
uniform bool uHasTexture;
uniform float uAlphaCutoff;

out vec4 fragColor;

void main()
{
    if (uHasTexture)
    {
        float alpha = texture(uTexture, vUv).a;
        if (alpha <= uAlphaCutoff)
        {
            discard;
        }
    }
    fragColor = vec4(1.0);
}
