#version 410 core
in vec2 textureCoord;
out vec4 FragColor;

uniform sampler2D uDepthTex;
uniform vec2 uScreenSize;
uniform float uNear;
uniform float uFar;
uniform vec2 uTanFovHalf;

// Hemisphere kernel (Z >= 0 = toward camera in tangent space)
// Samples weighted toward center (higher Z) to concentrate near surface
const int KERNEL_SIZE = 16;
const vec3 KERNEL[16] = vec3[](
    vec3( 0.035,  0.000,  0.10), vec3(-0.058,  0.000,  0.20),
    vec3( 0.000,  0.045,  0.30), vec3( 0.000, -0.038,  0.40),
    vec3( 0.071,  0.071,  0.50), vec3(-0.071, -0.071,  0.60),
    vec3(-0.060,  0.060,  0.70), vec3( 0.060, -0.060,  0.80),
    vec3( 0.095,  0.000,  0.90), vec3(-0.085,  0.000,  0.15),
    vec3( 0.000,  0.100,  0.25), vec3( 0.000, -0.090,  0.35),
    vec3( 0.130,  0.130,  0.45), vec3(-0.130, -0.130,  0.55),
    vec3(-0.120,  0.120,  0.65), vec3( 0.120, -0.120,  0.75)
);

const float SAMPLE_RADIUS = 0.6;  // physical radius in meters
const float DEPTH_BIAS    = 0.015; // 1.5 cm - prevents surface self-occlusion
const float MAX_DISTANCE  = 1.2;  // 1.2 m range check

float getLinearDepth(float depth) {
    float z = depth * 2.0 - 1.0;
    return (2.0 * uNear * uFar) / (uFar + uNear - z * (uFar - uNear));
}

vec3 getViewPos(vec2 uv) {
    float d  = texture(uDepthTex, uv).r;
    float ld = getLinearDepth(d);
    return vec3((uv * 2.0 - 1.0) * uTanFovHalf * ld, -ld);
}

// Reconstruct view-space normal from depth using finite differences
// cross(right, up) gives +Z (toward camera) for front-facing surfaces
vec3 getViewNormal(vec2 uv) {
    vec2 texel = 1.0 / uScreenSize;
    vec3 p  = getViewPos(uv);
    vec3 pr = getViewPos(uv + vec2(texel.x, 0.0));
    vec3 pu = getViewPos(uv + vec2(0.0, texel.y));
    return normalize(cross(pr - p, pu - p));
}

// Build TBN aligning Z with normal (hemisphere toward camera)
mat3 buildTBN(vec3 n) {
    vec3 up = (abs(n.x) < 0.999) ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
    vec3 t  = normalize(cross(up, n));
    vec3 b  = cross(n, t);
    return mat3(t, b, n);
}

void main() {
    float depth = texture(uDepthTex, textureCoord).r;
    if (depth >= 0.999) {
        FragColor = vec4(1.0);
        return;
    }

    vec3  viewPos  = getViewPos(textureCoord);
    vec3  normal   = getViewNormal(textureCoord);
    mat3  TBN      = buildTBN(normal);
    float linearD  = -viewPos.z;

    float occlusion = 0.0;

    for (int i = 0; i < KERNEL_SIZE; i++) {
        // Orient sample along surface normal into hemisphere toward camera
        vec3 sampleDir = TBN * KERNEL[i];
        vec3 samplePos = viewPos + sampleDir * SAMPLE_RADIUS;

        // Skip samples behind camera
        if (-samplePos.z < 0.01) continue;

        // Project sample to screen UV
        vec2 sampleUV = (samplePos.xy / (-samplePos.z * uTanFovHalf)) * 0.5 + 0.5;
        if (any(lessThan(sampleUV, vec2(0.0))) || any(greaterThan(sampleUV, vec2(1.0)))) continue;

        float distGeom   = getLinearDepth(texture(uDepthTex, sampleUV).r);
        float distSample = -samplePos.z;

        // diff > 0: geometry is in front of sample = sample is inside geometry = occluded
        float diff = distSample - distGeom;

        // Range check: ignore geometry far from original surface (avoids false hits on background)
        float rangeCheck = smoothstep(0.0, 1.0, MAX_DISTANCE / (abs(linearD - distGeom) + 0.001));

        if (diff > DEPTH_BIAS) {
            occlusion += rangeCheck;
        }
    }

    occlusion /= float(KERNEL_SIZE);
    // Slight power curve to punch up dark areas
    occlusion  = pow(occlusion, 1.2);

    FragColor = vec4(vec3(1.0 - occlusion), 1.0);
}
