// ShaderMod v2：天空图（对应 Derivative program/Deferred0.glsl 的"原始天空图"部分 + deferred.vsh 的光照强度）。
// 输出 256×256：x < 255 为按 ProjectSky 展开的物理大气天空；x = 255 列的 0~3 行依次为
// 直射光、天空光、太阳光、月光强度（其余通道供后续通道读取）。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"

uniform sampler3D atmosphereLut;

#include "/lib/Atmosphere/Atmosphere.glsl"

layout(location = 0) out vec4 skyOut;

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy);

	if (texel.x == skyCaptureRes.x) {
		vec3 camera = vec3(0.0, planetRadius + eyeAltitude, 0.0);
		vec3 sunIlluminance, moonIlluminance;
		vec3 skyIlluminance = GetSunAndSkyIrradiance(atmosphereModel, camera, worldSunVector, sunIlluminance, moonIlluminance);
		vec3 directIlluminance = sunIlluminance + moonIlluminance;

		vec3 value = vec3(0.0);
		if (texel.y == 0) value = directIlluminance;
		else if (texel.y == 1) value = skyIlluminance;
		else if (texel.y == 2) value = sunIlluminance;
		else if (texel.y == 3) value = moonIlluminance;

		skyOut = vec4(clamp16F(value), 1.0);
	} else {
		vec3 worldDir = UnprojectSky(gl_FragCoord.xy * rcp(vec2(skyCaptureRes)));
		vec3 transmittance;
		skyOut = vec4(clamp16F(GetSkyRadiance(atmosphereModel, worldDir, worldSunVector, transmittance) * 20.0), 1.0);
	}
}
