// Simplified BSD License (BSD-2-Clause)
// Copyright (c) 2024, Aditiya Afrizal, The OpenBVE Project

#version 410 core

// ─── Vertex attributes ───────────────────────────────────────────────────────
layout(location = 0) in vec3 iPosition;
layout(location = 2) in vec2 iUv;
layout(location = 4) in ivec3 iMatrixChain;

// ─── Uniforms ─────────────────────────────────────────────────────────────────
// World-space position of the reflective probe (the object being reflected in).
uniform vec3  uProbePosition;
// 0 = front hemisphere (+Z face), 1 = back hemisphere (-Z face).
uniform int   uFace;
// Near/far clip planes for the paraboloid projection.
uniform float uNear;
uniform float uFar;
// Texture matrix for animated UV scrolling.
uniform mat4  uTextureMatrix;
uniform mat4  uModelMatrix;

// Animation matrix UBO (shared layout with shadow_depth and default shaders).
layout(std140) uniform matrices
{
    mat4 uMatrix[128];
};

// ─── Outputs ──────────────────────────────────────────────────────────────────
out vec2  vUv;
out float vDepth;   // Linear depth 0..1 for early-exit in the frag shader.

// ─── Helpers ──────────────────────────────────────────────────────────────────
vec3 transformVector(vec3 v, int idx)
{
    return vec3(uMatrix[idx] * vec4(v, 1.0));
}

// Paraboloid projection following the Heidrich & Seidel (1998) formulation.
// Input: eye-space position relative to the probe.
// Output: clip-space position for the front (face=0) or back (face=1) paraboloid.
vec4 paraboloidProject(vec3 dir)
{
    // Mirror Z for the back face.
    if (uFace == 1) dir.z = -dir.z;

    float len = length(dir);
    if (len < 1e-6) len = 1e-6;
    vec3 n = dir / len;

    // Heidrich–Seidel paraboloid mapping.
    float denom = n.z + 1.0;
    if (abs(denom) < 1e-5) denom = sign(denom) * 1e-5;

    float x = n.x / denom;
    float y = n.y / denom;

    // Linearised depth in NDC [-1, 1].
    float linearDepth = (len - uNear) / (uFar - uNear);
    float z = linearDepth * 2.0 - 1.0;

    return vec4(x, y, z, 1.0);
}

void main()
{
    vec3 pos = iPosition;

    // Apply animation matrix chain (same logic as default.vert and shadow_depth.vert).
    if (iMatrixChain.x != 0)
    {
        int matIdx0 = (iMatrixChain.x & (0xff << 24)) >> 24;
        int matIdx1 = (iMatrixChain.x >> 16) & 0xff;
        int matIdx2 = (iMatrixChain.x & 0xff00) >> 8;
        int matIdx3 = (iMatrixChain.x & 0xff);
        if (matIdx0 >= 0 && matIdx0 < 255) pos = transformVector(pos, matIdx0);
        if (matIdx1 >= 0 && matIdx1 < 255) pos = transformVector(pos, matIdx1);
        if (matIdx2 >= 0 && matIdx2 < 255) pos = transformVector(pos, matIdx2);
        if (matIdx3 >= 0 && matIdx3 < 255) pos = transformVector(pos, matIdx3);
    }
    if (iMatrixChain.y != 0)
    {
        int matIdx0 = (iMatrixChain.y & (0xff << 24)) >> 24;
        int matIdx1 = (iMatrixChain.y >> 16) & 0xff;
        int matIdx2 = (iMatrixChain.y & 0xff00) >> 8;
        int matIdx3 = (iMatrixChain.y & 0xff);
        if (matIdx0 >= 0 && matIdx0 < 255) pos = transformVector(pos, matIdx0);
        if (matIdx1 >= 0 && matIdx1 < 255) pos = transformVector(pos, matIdx1);
        if (matIdx2 >= 0 && matIdx2 < 255) pos = transformVector(pos, matIdx2);
        if (matIdx3 >= 0 && matIdx3 < 255) pos = transformVector(pos, matIdx3);
    }
    if (iMatrixChain.z != 0)
    {
        int matIdx0 = (iMatrixChain.z & (0xff << 24)) >> 24;
        int matIdx1 = (iMatrixChain.z >> 16) & 0xff;
        int matIdx2 = (iMatrixChain.z & 0xff00) >> 8;
        int matIdx3 = (iMatrixChain.z & 0xff);
        if (matIdx0 >= 0 && matIdx0 < 255) pos = transformVector(pos, matIdx0);
        if (matIdx1 >= 0 && matIdx1 < 255) pos = transformVector(pos, matIdx1);
        if (matIdx2 >= 0 && matIdx2 < 255) pos = transformVector(pos, matIdx2);
        if (matIdx3 >= 0 && matIdx3 < 255) pos = transformVector(pos, matIdx3);
    }

    // OpenBVE convention: negate Z.
    pos.z = -pos.z;

    // Vector from probe to vertex (camera-relative coordinate).
    vec3 dir = (uModelMatrix * vec4(pos, 1.0)).xyz;

    float len = length(dir);
    vDepth = clamp((len - uNear) / (uFar - uNear), 0.0, 1.0);

    vUv = (uTextureMatrix * vec4(iUv, 1.0, 1.0)).xy;
    gl_Position = paraboloidProject(dir);
}
