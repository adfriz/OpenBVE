#version 330 core
precision highp float;

in vec3 vWorldPos;
out vec4 fragColor;

uniform vec3 uRealSkySunDirection;
uniform vec3 uRealSkyCameraPos;
uniform float uRealSkyTime;
uniform vec2 uRealSkyResolution;

// Atmospheric Scattering & Volumetric Clouds
// (Optimized Raymarching with AMD/NaN safeguards)

const float cloudMinHeight = 1500.0;
const float cloudMaxHeight = 2500.0;

float hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

float noise(vec3 x) {
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0 + 113.0 * p.z;
    return mix(mix(mix(hash(n + 0.0), hash(n + 1.0), f.x),
                   mix(hash(n + 57.0), hash(n + 58.0), f.x), f.y),
               mix(mix(hash(n + 113.0), hash(n + 114.0), f.x),
                   mix(hash(n + 170.0), hash(n + 171.0), f.x), f.y), f.z);
}

float fbm(vec3 p) {
    float f = 0.5000 * noise(p); p = p * 2.02;
    f += 0.2500 * noise(p); p = p * 2.03;
    f += 0.1250 * noise(p); p = p * 2.01;
    f += 0.0625 * noise(p);
    return f;
}

vec3 getAtmosphere(vec3 dir) {
    float sun = max(dot(dir, uRealSkySunDirection), 0.0);
    vec3 skyColor = mix(vec3(0.05, 0.2, 0.5), vec3(0.4, 0.6, 0.9), pow(max(dir.y, 0.0), 0.5));
    skyColor += vec3(1.0, 0.8, 0.6) * pow(sun, 256.0); // Sun disk
    skyColor += vec3(1.0, 0.4, 0.2) * pow(sun, 8.0) * 0.5; // Glow
    return skyColor;
}

float getCloudDensity(vec3 p) {
    float d = fbm(p * 0.0005 + uRealSkyTime * 0.05);
    d = smoothstep(0.4, 0.8, d);
    float heightFade = smoothstep(cloudMinHeight, cloudMinHeight + 200.0, p.y) * 
                      (1.0 - smoothstep(cloudMaxHeight - 500.0, cloudMaxHeight, p.y));
    return d * heightFade;
}

void main() {
    // NaN safeguard for normalization
    vec3 viewDir = normalize(vWorldPos + vec3(1e-7));
    vec3 sky = getAtmosphere(viewDir);
    
    // Raymarching clouds
    vec3 cloudColor = vec3(0.0);
    float alpha = 0.0;
    
    // Division-by-zero safeguard for horizon
    if (viewDir.y > 1e-4) {
        float t = (cloudMinHeight - uRealSkyCameraPos.y) / viewDir.y;
        if (t > 0.0) {
            for(int i = 0; i < 16; i++) {
                vec3 p = uRealSkyCameraPos + viewDir * t;
                float d = getCloudDensity(p);
                if (d > 0.01) {
                    float light = max(dot(uRealSkySunDirection, vec3(0,1,0)), 0.2);
                    vec3 c = mix(vec3(0.6, 0.6, 0.7), vec3(1.0), d) * light;
                    cloudColor += c * d * (1.0 - alpha);
                    alpha += d * 0.2;
                }
                t += 100.0;
                if (alpha >= 1.0 || t > 20000.0) break;
            }
        }
    }
    
    vec3 finalColor = mix(sky, cloudColor, clamp(alpha, 0.0, 1.0));
    fragColor = vec4(finalColor, 1.0);
}
