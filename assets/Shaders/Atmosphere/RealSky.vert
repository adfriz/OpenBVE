#version 150

in vec3 iPosition;
out vec3 vWorldPos;

uniform mat4 uCurrentProjectionMatrix;
uniform mat4 uCurrentModelViewMatrix;

void main() {
    vWorldPos = iPosition;
    // Standard skybox transformation (centered around camera)
    vec4 pos = uCurrentProjectionMatrix * uCurrentModelViewMatrix * vec4(iPosition, 1.0);
    // Force to background (z=1)
    gl_Position = pos.xyww;
}
