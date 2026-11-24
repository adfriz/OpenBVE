//Display transform for the shared sRGB framebuffer, included via
//`#include "tonemap/tonemap.glsl"`. GLSL has no preprocessor, so
//AbstractShader.ResolveIncludes expands this before compilation.
//
//Kept separate from the BRDF because it is not a lighting concern: every
//shading path that wants filmic output goes through PBR_Output(), so adding
//another operator (AgX, Neutral, ...) means editing one file rather than
//every path that happens to tone map.

/// Real ACES (Stephen Hill RRT+ODT fit, same as three.js ACESFilmicToneMapping
/// and the OCIO ACES output transform family): proper Academy transfer with
/// sRGB in/out matrices, NOT the washed-out Narkowicz approximation.
/// (Contrast "variants" live in AgX/Neutral, not ACES; three.js parity is what
///  matters here since web viewers are the reference.) Exposure is 1.0.
vec3 PBR_ACES(vec3 x)
{
	const mat3 ACESInputMat = mat3(
		vec3(0.59719, 0.07600, 0.02840),
		vec3(0.35458, 0.90834, 0.13383),
		vec3(0.04823, 0.01566, 0.83777)
	);
	const mat3 ACESOutputMat = mat3(
		vec3(1.60475, -0.10208, -0.00327),
		vec3(-0.53108, 1.10813, -0.07276),
		vec3(-0.07367, -0.00605, 1.07602)
	);
	// three.js used a 1/0.6 viewing-environment tweak in its legacy-lights era
	// (see three.js #19621); current three.js and Blender apply no such
	// boost, and keeping it overexposes rims/highlights vs both.
	vec3 c = x;
	c = ACESInputMat * c;
	vec3 a = c * (c + 0.0245786) - 0.000090537;
	vec3 b = c * (0.983729 * c + 0.4329510) + 0.238081;
	c = a / b;
	c = ACESOutputMat * c;
	return clamp(c, 0.0, 1.0);
}

/// Scene-linear to framebuffer: tone map, then the approx gamma 2.2 encode.
/// Web viewers render glTF in linear space with ACES tone mapping; we keep the
/// shared sRGB framebuffer and convert at the edges, so both steps belong
/// together and must not be applied independently.
vec3 PBR_Output(vec3 linear)
{
	return pow(clamp(PBR_ACES(linear), 0.0, 1.0), vec3(1.0 / 2.2));
}