#version 410 core
in vec2 textureCoord;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform sampler2D uSSAOTex; 
uniform bool uUseSSAOTex;

void main() {
    vec4 color = texture(uSceneTex, textureCoord);
    if (uUseSSAOTex) {
        float ao = texture(uSSAOTex, textureCoord).r;
        color.rgb *= ao;
    }
    FragColor = color;
}
