#version 330 core

layout(location = 0) in vec3 iPosition;

uniform mat4 uCurrentProjectionMatrix;
uniform mat4 uCurrentModelViewMatrix;

out vec3 vWorldPos;

void main() {
    vWorldPos = iPosition;
    // Standard skybox transformation (centered around camera)
    // Strip translation from view matrix — sky must NOT move with camera position,
    // only with camera rotation. Keep only the 3x3 rotation block.
    mat4 rotView = uCurrentModelViewMatrix;
    rotView[3] = vec4(0.0, 0.0, 0.0, 1.0);
    vec4 pos = uCurrentProjectionMatrix * rotView * vec4(iPosition, 1.0);
    // Force to background (z = 1) so the skybox is always behind the scene.
    gl_Position = pos.xyww;
}
