#version 330 core
// RealSky Fragment Shader - LEGACY (OpenGL 3.3 / GLSL 330)
// Rewired: transmittance compositing, better atmosphere, better cloud shading.

in vec3 vWorldPos;
out vec4 fragColor;

uniform vec3  uRealSkySunDirection;
uniform vec3  uRealSkyCameraPos;
uniform float uRealSkyTime;
uniform vec2  uRealSkyResolution;
uniform int   uRealSkyMode = 3;

const float cloudMinHeight = 1500.0;
const float cloudMaxHeight = 2500.0;

// ---- Procedural noise -----------------------------------------------
float hash(float n) { return fract(sin(n) * 43758.5453123); }

float noise(vec3 x) {
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0 + 113.0 * p.z;
    return mix(
        mix(mix(hash(n + 0.0),   hash(n + 1.0),   f.x),
            mix(hash(n + 57.0),  hash(n + 58.0),  f.x), f.y),
        mix(mix(hash(n + 113.0), hash(n + 114.0), f.x),
            mix(hash(n + 170.0), hash(n + 171.0), f.x), f.y),
        f.z);
}

float fbm(vec3 p) {
    float v  = 0.5000 * noise(p); p *= 2.02;
    v += 0.2500 * noise(p); p *= 2.03;
    v += 0.1250 * noise(p); p *= 2.01;
    v += 0.0625 * noise(p);
    return v;
}

// ---- Atmosphere -----------------------------------------------
vec3 getAtmosphere(vec3 dir) {
    vec3  sunDir   = normalize(uRealSkySunDirection);
    float cosTheta = dot(dir, sunDir);
    float sunElev  = sunDir.y;
    float elev     = max(dir.y, 0.0);

    // Zenith-to-horizon gradient
    vec3 zenith  = vec3(0.18, 0.42, 0.85);
    vec3 horizon = vec3(0.55, 0.75, 0.95);
    vec3 col = mix(horizon, zenith, pow(elev, 0.5));

    // Sunset tint
    float sunsetF = clamp(1.0 - sunElev * 3.0, 0.0, 1.0);
    float horizF  = exp(-elev * 5.0);
    vec3 sunsetTint = mix(vec3(1.0, 0.85, 0.70), vec3(1.0, 0.38, 0.10), sunsetF);
    col = mix(col, col * sunsetTint, horizF * sunsetF * 0.8);

    // Mie glow
    float mie = pow(max(cosTheta, 0.0), 120.0) * 0.4;
    col += sunsetTint * mie * horizF;

    // Sun disk
    float disk = smoothstep(0.9995, 0.9998, cosTheta) * 15.0;
    vec3 sunCol = mix(vec3(1.0, 0.95, 0.80), vec3(1.0, 0.55, 0.15), sunsetF);
    col += sunCol * disk;

    // Fade to black only BELOW horizon (not above)
    float belowHorizon = 1.0 - smoothstep(-0.06, 0.0, dir.y);
    col = mix(col, vec3(0.0), belowHorizon);
    return max(col, vec3(0.0));
}

// ---- Cloud density (multi-octave) ------------------------------------
float cloudDensity(vec3 p) {
    vec3 drift = vec3(uRealSkyTime * 0.006, 0.0, uRealSkyTime * 0.003);
    vec3 uv    = p * 0.000220 + drift;

    float d = fbm(uv * 4.0);
    d += 0.5 * fbm(uv * 8.5 + vec3(0.31, 0.0, 0.57));
    d /= 1.5;
    d = smoothstep(0.40, 0.75, d);

    float hFade = smoothstep(cloudMinHeight, cloudMinHeight + 250.0, p.y)
                * (1.0 - smoothstep(cloudMaxHeight - 350.0, cloudMaxHeight, p.y));
    return d * hFade;
}

// ---- Main -------------------------------------------------------
void main() {
    vec3 viewDir = normalize(vWorldPos + vec3(1e-7));
    vec3 sky     = getAtmosphere(viewDir);

    vec3  sunDir  = normalize(uRealSkySunDirection);
    float sunElev = sunDir.y;
    vec3 sunColor = mix(vec3(1.0, 0.90, 0.75), vec3(1.0, 0.50, 0.15),
                        clamp(1.0 - sunElev * 2.5, 0.0, 1.0));
    vec3 ambColor = mix(vec3(0.38, 0.52, 0.80), vec3(0.60, 0.72, 0.88),
                        clamp(sunElev + 0.2, 0.0, 1.0));

    // Beer-Lambert cloud integration
    vec3  cloudAccum = vec3(0.0);
    float transmit   = 1.0;

    if (viewDir.y > 5e-4 && uRealSkyMode > 0) {
        float tMin   = (cloudMinHeight - uRealSkyCameraPos.y) / viewDir.y;
        float tMax   = (cloudMaxHeight - uRealSkyCameraPos.y) / viewDir.y;
        float tEnter = max(0.0, tMin);
        float tExit  = min(20000.0, tMax);

        if (tEnter < tExit) {
            float stepSize = (tExit - tEnter) / 16.0;
            float t = tEnter;

            for (int i = 0; i < 16; i++) {
                if (transmit < 0.01) { break; }

                vec3  p = uRealSkyCameraPos + viewDir * t;
                float d = cloudDensity(p);

                if (d > 0.005) {
                    float shadow = 0.0;
                    for (int j = 0; j < 4; j++) {
                        vec3 lp = p + sunDir * float(j + 1) * stepSize * 0.6;
                        shadow += cloudDensity(lp);
                    }
                    shadow = exp(-shadow * 0.5);

                    float powder   = 1.0 - exp(-d * stepSize * 0.005);
                    float dirLight = max(dot(sunDir, vec3(0.0, 1.0, 0.0)), 0.0);
                    vec3  cloudLit = ambColor * 0.45
                                   + sunColor * (shadow * (dirLight + 0.05)) * powder;

                    float sigma = d * 0.004 * stepSize;
                    cloudAccum += cloudLit * sigma * transmit;
                    transmit   *= exp(-sigma);
                }

                t += stepSize;
                if (t > tExit) { break; }
            }
        }
    }

    vec3 finalColor = sky * transmit + cloudAccum;
    fragColor = vec4(finalColor, 1.0);
}
