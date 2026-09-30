// ShaderMod v2：在游戏画天空的位置（绘制顺序 -100）画 Derivative 的天空、云、星星和月亮。
// 画进游戏的 8 位场景渲染目标：值 = sRGB(天空亮度 × 曝光 / vanillaGain)，
// 之后 Deferred 用相同换算还原成 HDR；雨、雪等仍由游戏正常画在上面。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"
#include "/lib/Head/Functions.inc"

uniform sampler2D skyMap;
uniform sampler2D cloudDome;
uniform sampler2D heightMap;
uniform sampler3D atmosphereLut;
uniform vec2 viewSize;
uniform float vanillaGain;

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;

#include "/lib/Atmosphere/Atmosphere.glsl"
#include "/lib/Head/SkyCommon.glsl"

layout(location = 0) out vec4 sceneOut;

void main() {
	vec2 screenCoord = gl_FragCoord.xy / viewSize;
	vec3 worldDir = ScreenToWorldDir(screenCoord);

	vec4 clouds = SampleCloudDome(worldDir);
	vec3 sky = SampleSkyRadiance(worldDir) * clouds.a + clouds.rgb;

	vec3 transmittance = GetTransmittance(worldDir);
	if (maxOf(transmittance) > 1e-4) {
		vec3 moonStars = RenderMoonReflection(worldDir, worldSunVector) * MoonFlux * 60.0;
		moonStars += RenderStars(worldDir);
		sky += moonStars * remap(minTransmittance, 1.0, clouds.a) * transmittance;
	}

	sceneOut = vec4(LinearToSRGB(saturate(sky * exposure / vanillaGain)), 1.0);
}
