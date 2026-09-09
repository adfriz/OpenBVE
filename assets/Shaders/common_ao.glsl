// Shared AO math for compute kernels.
// A chunk is not a standalone shader: it carries no #version line and must be
// pulled in with #include "common_ao.glsl" BELOW the consumer's own #version
// directive. All helpers are pure (matrices/planes passed as parameters) so
// the chunk declares no uniforms and works in any 430 compute shader.
// Used by: ao_sao, ao_gtao, ao_depth, ao_composite.

const float PI = 3.14159265359;
const float PI2 = 6.28318530718;
const float PHI = 1.61803398875; // golden ratio for jitter
const float EPSILON = 0.01;      // pixel-too-close: avoid self-shadow

float HashRot01(ivec2 px)
{
	// XOR-hash rotation from the SAO paper (replaces a 4x4 noise texture, saves 1 fetch).
	uint h = uint(px.x) * 374761393u + uint(px.y) * 668265263u;
	h ^= h >> 13u;
	h *= 1274126177u;
	h ^= h >> 16u;
	return float(h & 0xFFFFFFu) / 16777216.0;
}

// Hilbert-curve index + R2 quasirandom pair (Intel XeGTAO shape, MIT):
// better-distributed (less harsh) pixel noise than white-noise hashing.
// Only the GTAO kernel uses it for now; SAO keeps HashRot01 (looks clean).
uint HilbertIndex(uint posX, uint posY)
{
	uint index = 0u;
	for (uint curLevel = 32u; curLevel > 0u; curLevel /= 2u)
	{
		uint regionX = ((posX & curLevel) != 0u) ? 1u : 0u;
		uint regionY = ((posY & curLevel) != 0u) ? 1u : 0u;
		index += curLevel * curLevel * ((3u * regionX) ^ regionY);
		if (regionY == 0u)
		{
			if (regionX == 1u)
			{
				posX = 63u - posX;
				posY = 63u - posY;
			}
			uint temp = posX;
			posX = posY;
			posY = temp;
		}
	}
	return index;
}

vec2 R2Noise(uint hilbertIndex)
{
	return fract(vec2(0.5) + float(hilbertIndex) * vec2(0.7548776662, 0.5698402910));
}

// Depth-discontinuity edges from linear view-Z taps (Intel XeGTAO shape):
// 1 = no edge (free smoothing), 0 = hard edge (blocked). Gates at ~1.1%
// relative depth difference, slope-adjusted. Packed 2 bits per direction
// (LRTB) into R8 for the denoise pass.
vec4 ComputeEdges(float centerZ, float leftZ, float rightZ, float topZ, float bottomZ)
{
	vec4 e = vec4(leftZ, rightZ, topZ, bottomZ) - centerZ;
	float slopeLR = (e.y - e.x) * 0.5;
	float slopeTB = (e.w - e.z) * 0.5;
	vec4 eAdj = e + vec4(slopeLR, -slopeLR, slopeTB, -slopeTB);
	e = min(abs(e), abs(eAdj));
	return clamp(vec4(1.25 - e / max(centerZ * 0.011, 1e-6)), 0.0, 1.0);
}

float PackEdges(vec4 e)
{
	vec4 q = floor(clamp(e, 0.0, 1.0) * 2.9 + 0.5);
	return dot(q, vec4(64.0, 16.0, 4.0, 1.0) / 255.0);
}

vec4 UnpackEdges(float packedVal)
{
	uint p = uint(packedVal * 255.5);
	vec4 e;
	e.x = float((p >> 6u) & 0x03u) / 3.0;
	e.y = float((p >> 4u) & 0x03u) / 3.0;
	e.z = float((p >> 2u) & 0x03u) / 3.0;
	e.w = float(p & 0x03u) / 3.0;
	return clamp(e, 0.0, 1.0);
}

vec3 ViewPosFromLinear(vec2 uv, float viewZ, mat4 invProj)
{
	// Reconstruct the view-space position from a positive -z distance.
	// Ray direction = unprojected far-plane point (origin at 0), scaled so -z == viewZ.
	vec4 clip = vec4(uv * 2.0 - 1.0, 1.0, 1.0);
	vec4 d4 = invProj * clip;
	vec3 dir = d4.xyz / max(abs(d4.w), 1e-6);
	float dz = max(-dir.z, 1e-6);
	float t = viewZ / dz;
	return dir * t;
}

vec3 DecodeNormal(vec3 enc)
{
	// ao_depth encodes view-space normals 0..1; degenerate texels face the viewer.
	vec3 n = enc * 2.0 - vec3(1.0);
	float len = length(n);
	if (len < 1e-4)
	{
		return vec3(0.0, 0.0, 1.0);
	}
	return n / len;
}

float ComputeRadiusPix(float radiusM, mat4 proj, vec2 res, float viewZ)
{
	// World radius -> pixel radius: r_px = R * (proj11*H/2) / viewZ, clamped.
	float proj11 = abs(proj[1][1]) > 1e-6 ? abs(proj[1][1]) : 1.0;
	float projScale = proj11 * 0.5 * max(res.y, 1.0);
	float radiusPix = radiusM * projScale / max(viewZ, 1e-3);
	radiusPix = min(radiusPix, min(res.x, res.y) * 0.5);
	return max(radiusPix, 1.0);
}

float LinearizeDepth01(float d, float near, float far)
{
	// Precise SAO paper S2.1 formula for OpenGL perspective projection:
	// NDC z = 2*d-1, viewZ = (near*far) / (far - d*(far-near)) (positive).
	// d=0 -> near, d=1 -> far.
	float denom = far - d * (far - near);
	denom = max(denom, 1e-6);
	return (near * far) / denom;
}

bool IsSkyDepth01(float d)
{
	// Raw depth buffer value at the far plane: downstream AO treats it as AO=1.
	return d >= 0.99999;
}

bool IsSkyLinear(float viewZ, float far)
{
	// Linearized view-Z at the far plane: never occluded, never an occluder.
	return viewZ >= far * 0.999;
}
