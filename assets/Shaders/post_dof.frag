#version 410 core
precision highp float;

// Bokeh DOF based on Dennis Gustafsson's scatter-as-gather trick
// (blog.voxagon.se/2018/05/04/bokeh-depth-of-field-in-single-pass.html):
// a spiral tap contributes only when its own CoC reaches this pixel, and non-
// contributing taps fold the running average back in. Background CoC is clamped to
// twice the centre CoC so sharp foregrounds do not get smeared. HDR pow(4) ->
// accumulate -> pow(1/4) preserves bright circular bokeh discs.
// uOutputCoc > 0 switches to the half-resolution variant used with the composite
// pass; the full-resolution path (default) writes its result directly.

uniform sampler2D uSource;
uniform sampler2D uDepth;
uniform float uStrength;
uniform float uOutputCoc;   // >0: half-res pass, write coverage in alpha
uniform float uFocus;       // focus distance in world units
uniform float uFocusRange;  // world-unit width of the focus falloff
uniform float uAperture;    // CoC multiplier
uniform float uMaxBlur;     // max radius in pixels
uniform float uNearBoost;   // foreground CoC multiplier
uniform float uNear;
uniform float uFar;
uniform float uTexelX;
uniform float uTexelY;

in vec2 vUv;
out vec4 fragColor;

const int MAX_TAPS = 48;
const float GOLDEN_ANGLE = 2.39996323;
const float TWO_PI = 6.28318530718;

float InterleavedGradientNoise(vec2 p)
{
	vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
	return fract(magic.z * fract(dot(p, magic.xy)));
}

float LinearDepth(float d)
{
	float z = d * 2.0 - 1.0;
	return (2.0 * uNear * uFar) / (uFar + uNear - z * (uFar - uNear));
}

float CircleOfConfusion(float d)
{
	float delta = d - uFocus;
	float boost = delta < 0.0 ? uNearBoost : 1.0;
	return clamp(abs(delta) / max(uFocusRange, 0.001) * uAperture * boost, 0.0, 1.0) * uMaxBlur;
}

void main()
{
	vec4 source = texture(uSource, vUv);
	float centerDepth = LinearDepth(texture(uDepth, vUv).r);
	float coc = CircleOfConfusion(centerDepth);
	bool halfRes = uOutputCoc > 0.5;
	if (uStrength <= 0.0 || coc < 0.75)
	{
		fragColor = halfRes ? vec4(source.rgb, 0.0) : source;
		return;
	}

	// Small blurs stay cheap; large radii get enough samples to avoid visible dots.
	int taps = coc < 4.0 ? 12 : (coc < 9.0 ? 24 : MAX_TAPS);
	float radScale = coc / sqrt(float(taps - 1));
	float rotation = InterleavedGradientNoise(gl_FragCoord.xy) * TWO_PI;

	vec3 color = source.rgb * source.rgb * source.rgb * source.rgb; // HDR lift
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
				float sampleDepth = LinearDepth(texture(uDepth, uv).r);
				float sampleCoC = CircleOfConfusion(sampleDepth);
				if (sampleDepth > centerDepth)
				{
					sampleCoC = min(sampleCoC, coc * 2.0);
				}
				float m = smoothstep(radius - 0.5, radius + 0.5, sampleCoC);
				vec3 tap = texture(uSource, uv).rgb;
				vec3 hdr = tap * tap * tap * tap;
				// non-contributing taps keep the running average unchanged
				color += mix(color / total, hdr, m);
				total += 1.0;
			}
		}
	}

	if (total < 1.5)
	{
		fragColor = halfRes ? vec4(source.rgb, 0.0) : source;
		return;
	}
	vec3 blurred = pow(color / total, vec3(0.25));
	if (halfRes)
	{
		float n0 = CircleOfConfusion(LinearDepth(texture(uDepth, vUv + vec2(uTexelX, 0.0)).r));
		float n1 = CircleOfConfusion(LinearDepth(texture(uDepth, vUv - vec2(uTexelX, 0.0)).r));
		float n2 = CircleOfConfusion(LinearDepth(texture(uDepth, vUv + vec2(0.0, uTexelY)).r));
		float n3 = CircleOfConfusion(LinearDepth(texture(uDepth, vUv - vec2(0.0, uTexelY)).r));
		float coverage = clamp((coc + n0 + n1 + n2 + n3) * 0.2 / 2.0, 0.0, 1.0);
		fragColor = vec4(blurred, coverage);
		return;
	}
	fragColor = vec4(mix(source.rgb, blurred, uStrength), source.a);
}
