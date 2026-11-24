#version 410 core
precision highp float;

// Bokeh DOF: Gustafsson scatter-as-gather (blog.voxagon.se/2018/05/04/...)
// with ideas from AMD FidelityFX Depth of Field 1.1:
//  - signed thin-lens CoC computed straight from raw hardware depth
//  - bilateral CoC-difference weighting in the half-resolution coverage pass
//  - 3x3 median post-filter in the composite pass against fireflies
// Non-contributing taps fold the running average back in, which keeps the
// blur continuous without any random sample weighting.

uniform sampler2D uSource;
uniform sampler2D uSourceMip; // SPD-generated mip chain of the scene, unit 2
uniform sampler2D uDepth;
uniform float uStrength;
uniform float uOutputCoc;   // >0: half-res pass, write coverage in alpha
uniform float uFocus;       // focus distance in world units
uniform float uFocusRange;  // world-unit width of the focus falloff
uniform float uAperture;    // CoC gain
uniform float uMaxBlur;     // max radius in pixels
uniform float uNear;
uniform float uFar;
uniform float uTexelX;
uniform float uTexelY;
uniform int uQuality;       // 0 Auto, 1 Low, 2 Medium, 3 High

in vec2 vUv;
out vec4 fragColor;

const int MAX_TAPS = 48;
const float GOLDEN_ANGLE = 2.39996323;
const float TWO_PI = 6.28318530718;
const float GAMMA = 2.2;

float InterleavedGradientNoise(vec2 p)
{
	vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
	return fract(magic.z * fract(dot(p, magic.xy)));
}

// Thin-lens CoC. 1/viewDistance is affine in raw z-buffer depth, so no
// perspective divide is needed. Sign carries the field: positive near,
// negative far. Near-field blur grows faster for the same distance offset,
// which makes the old Near boost heuristic unnecessary.
float SignedCoc(float rawDepth)
{
	float invDist = (uFar - rawDepth * (uFar - uNear)) / max(uNear * uFar, 0.000001);
	float invFocus = 1.0 / max(uFocus, 0.001);
	float invLimit = 1.0 / max(uFocus + uFocusRange, 0.001);
	float gain = uAperture / max(abs(invFocus - invLimit), 0.000001);
	return (invDist - invFocus) * gain;
}

float Radius(float signedCoc)
{
	return min(abs(signedCoc), 1.0) * uMaxBlur;
}

void main()
{
	vec4 source = texture(uSource, vUv);
	float centerSigned = SignedCoc(texture(uDepth, vUv).r);
	float coc = Radius(centerSigned);
	bool halfRes = uOutputCoc > 0.5;
	if (uStrength <= 0.0 || coc < 0.75)
	{
		fragColor = halfRes ? vec4(source.rgb, 0.0) : source;
		return;
	}

	// Auto scales the sample count with CoC; the other modes fix it outright.
	int taps = coc < 4.0 ? 12 : (coc < 9.0 ? 24 : MAX_TAPS);
	if (uQuality == 1) taps = 12;
	else if (uQuality == 2) taps = 24;
	else if (uQuality == 3) taps = MAX_TAPS;
	float radScale = coc / sqrt(float(taps - 1));
	float rotation = InterleavedGradientNoise(gl_FragCoord.xy) * TWO_PI;

	// FidelityFX mip approach: when tap spacing exceeds a pixel there is less than
	// one sample per pixel, so read the pre-filtered mip chain instead of mip 0.
	// This removes noise at large radii without adding taps.
	float mipLod = clamp(log2(max(radScale, 1.0)) - 0.5, 0.0, 2.0);

	// The compositor tone-maps before post FX, so uSource is gamma-encoded SDR.
	// Accumulate in linear light and convert back at the end.
	vec3 color = pow(max(source.rgb, vec3(0.0)), vec3(GAMMA));
	float total = 1.0; // tap 0 is the centre pixel itself
	for (int i = 1; i < MAX_TAPS; i++)
	{
		if (i < taps)
		{
			float radius = radScale * sqrt(float(i));
			float angle = float(i) * GOLDEN_ANGLE + rotation;
			vec2 uv = vUv + vec2(cos(angle) * radius * uTexelX, sin(angle) * radius * uTexelY);

			if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0))))
			{
				float sampleSigned = SignedCoc(texture(uDepth, uv).r);
				float sampleRadius = Radius(sampleSigned);
				// A sample behind the centre cannot spill more blur than the centre has.
				if (sampleSigned < centerSigned)
				{
					sampleRadius = min(sampleRadius, coc * 2.0);
				}
				float m = smoothstep(radius - 0.5, radius + 0.5, sampleRadius);
				vec3 lin = pow(max(textureLod(uSourceMip, uv, mipLod).rgb, vec3(0.0)), vec3(GAMMA));
				// non-contributing taps keep the running average unchanged
				color += mix(color / total, lin, m);
				total += 1.0;
			}
		}
	}

	if (total < 1.5)
	{
		fragColor = halfRes ? vec4(source.rgb, 0.0) : source;
		return;
	}
	vec3 blurred = pow(max(color / total, vec3(0.0)), vec3(1.0 / GAMMA));
	if (halfRes)
	{
		// FidelityFX bilateral downsample rule: a neighbour more than one pixel
		// of CoC radius away from the centre gets zero weight. Same CoC means
		// this reduces to a plain average.
		float r0 = Radius(SignedCoc(texture(uDepth, vUv + vec2(uTexelX, 0.0)).r));
		float r1 = Radius(SignedCoc(texture(uDepth, vUv - vec2(uTexelX, 0.0)).r));
		float r2 = Radius(SignedCoc(texture(uDepth, vUv + vec2(0.0, uTexelY)).r));
		float r3 = Radius(SignedCoc(texture(uDepth, vUv - vec2(0.0, uTexelY)).r));
		float w0 = abs(r0 - coc) > 1.0 ? 0.0 : 1.0;
		float w1 = abs(r1 - coc) > 1.0 ? 0.0 : 1.0;
		float w2 = abs(r2 - coc) > 1.0 ? 0.0 : 1.0;
		float w3 = abs(r3 - coc) > 1.0 ? 0.0 : 1.0;
		float sum = coc + r0 * w0 + r1 * w1 + r2 * w2 + r3 * w3;
		float weight = 1.0 + w0 + w1 + w2 + w3;
		float coverage = clamp(sum / weight / 2.0, 0.0, 1.0);
		fragColor = vec4(blurred, coverage);
		return;
	}
	fragColor = vec4(mix(source.rgb, blurred, uStrength), source.a);
}
