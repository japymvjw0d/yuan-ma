// ShaderMod v2：体积光 / 体积雾（对应 Derivative world0/composite.fsh 的后半部分），半分辨率。
// rgb = 散射光，a = 透射率。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"
#include "/lib/Head/Functions.inc"

uniform sampler2D depthtex0;    // 含水面的深度
uniform sampler2D noisetex;
uniform sampler2DShadow shadowtex1;
uniform sampler3D atmosphereLut;

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;

#include "/lib/Head/Noise.inc"
#include "/lib/Atmosphere/Atmosphere.glsl"
#include "/lib/Atmosphere/VolumetricFog.glsl"

layout(location = 0) out vec4 fogData;

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy) * 2;
	vec2 screenCoord = (vec2(texel) + 0.5) * screenPixelSize;

	float depth = texelFetch(depthtex0, texel, 0).x;
	vec3 viewPos = ScreenToViewSpace(vec3(screenCoord, depth));
	vec3 worldPos = mat3(gbufferModelViewInverse) * viewPos;
	vec3 worldDir = normalize(worldPos);

	float dither = InterleavedGradientNoise(gl_FragCoord.xy);

	fogData = vec4(0.0, 0.0, 0.0, 1.0);
	if (isEyeInWater == 0) fogData = CalculateVolumetricFog(worldPos, worldDir, dither);
	else fogData.rgb = UnderwaterVolumetricLight(worldPos, worldDir, dither);

	float transmittance = fogData.a;
	if (isnan(transmittance) || isinf(transmittance)) transmittance = 1.0;
	fogData = vec4(SanitizeHdr(fogData.rgb), saturate(transmittance));
}
