#version 430 core
/*
 * RealSky Fragment Shader (OpenGL 4.3 / GLSL 430)
 * -------------------------------------------------------------
 * In the OpenGL 4.3 pipeline the heavy atmospheric + volumetric
 * cloud raymarching has been moved to a compute shader
 * (RealSky.comp) which writes its result to an image2D. This
 * fragment shader therefore only has to:
 *
 *   1. Sample the pre-computed sky/cloud texture with a linear
 *      sampler.
 *   2. Output the colour.
 *
 * The vertex shader passes through the skybox-cube world-space
 * position so that, if the compute pass is unavailable (older
 * GPU, or OpenGL 4.3 disabled), the renderer can swap in the
 * legacy single-pass fragment shader at runtime.
 */
in vec3 vWorldPos;
out vec4 fragColor;

layout(binding = 0) uniform sampler2D uSkyTexture;   // compute-shader output
uniform vec2 uRealSkyResolution;                      // screen dimensions

void main() {
    // Convert the skybox position to a screen UV by using gl_FragCoord.
    // gl_FragCoord is in window space, so we just divide by the resolution.
    vec2 uv = gl_FragCoord.xy / uRealSkyResolution;
    fragColor = texture(uSkyTexture, uv);
}
