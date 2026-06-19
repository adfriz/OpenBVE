#version 430 core
/*
 * RealSky Vertex Shader (OpenGL 4.3 / GLSL 430)
 * -------------------------------------------------------------
 * Same skybox-cube transformation as before, but upgraded to
 * GLSL 430 so the program can link together with the new compute
 * shader if needed, and so we can use explicit attribute / uniform
 * locations (an OpenGL 4.3 feature).
 */
layout(location = 0) in vec3 iPosition;

layout(location = 0) uniform mat4 uCurrentProjectionMatrix;
layout(location = 1) uniform mat4 uCurrentModelViewMatrix;

out vec3 vWorldPos;

void main() {
    vWorldPos = iPosition;
    // Standard skybox transformation (centered around camera)
    vec4 pos = uCurrentProjectionMatrix * uCurrentModelViewMatrix * vec4(iPosition, 1.0);
    // Force to background (z = 1) so the skybox is always behind the scene.
    gl_Position = pos.xyww;
}
