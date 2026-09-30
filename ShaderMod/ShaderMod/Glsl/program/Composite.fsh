// ShaderMod v2：水面、大气散射、边界雾、体积光合成（对应 Derivative world0/composite1.fsh + Gbuffers/Water.frag 的反射）。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"
#include "/lib/Head/Functions.inc"

uniform sampler2D deferredTex;  // Deferred 输出（HDR）
uniform sampler2D fogTex;       // 体积光（半分辨率）
uniform sampler2D depthtex0;    // 含水面的深度
uniform sampler2D depthtex1;    // 不含水面的深度
uniform sampler2D heightMap;
uniform sampler2D skyMap;
uniform sampler2D cloudDome;
uniform sampler2D noisetex;
uniform sampler3D atmosphereLut;

#define BORDER_FOG_FALLOFF 8.0

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;

#include "/lib/Head/Noise.inc"
#include "/lib/Atmosphere/Atmosphere.glsl"
#include "/lib/Head/SkyCommon.glsl"
#include "/lib/Surface/ScreenSpaceReflections.glsl"
#include "/lib/Water/WaterWave.glsl"
#include "/lib/Water/WaterFog.glsl"

layout(location = 0) out vec4 sceneOut;

// 半分辨率体积光的双边上采样（对应 Derivative 的 SpatialUpscale，去掉了时间抖动）
vec4 SpatialUpscale(in vec2 fragCoord, in float linearDepth) {
	ivec2 texel = ivec2(fragCoord * 0.5);
	ivec2 maxTexel = ivec2(screenSize * 0.5) - 1;

	float sigmaZ = 64.0 / max(linearDepth, 1e-3);
	vec4 total = vec4(0.0);
	float sumWeight = 0.0;

	for (int y = -1; y <= 1; ++y) {
		for (int x = -1; x <= 1; ++x) {
			ivec2 sampleTexel = clamp(texel + ivec2(x, y), ivec2(0), maxTexel);
			float sampleDepth = GetDepthLinear(texelFetch(depthtex0, sampleTexel * 2, 0).x);
			float weight = exp2(-abs(sampleDepth - linearDepth) * sigmaZ) * (x == 0 && y == 0 ? 2.0 : 1.0);
			weight = max(weight, 1e-6);
			total += texelFetch(fogTex, sampleTexel, 0) * weight;
			sumWeight += weight;
		}
	}
	return total / sumWeight;
}

vec3 ReconstructWorldNormal(in ivec2 texel, in vec3 viewPos) {
	ivec2 maxTexel = ivec2(screenSize) - 1;
	// 屏幕右 / 下边缘改用左 / 上侧的像素，避免差值为零
	ivec2 sx = texel.x < maxTexel.x ? ivec2(1, 0) : ivec2(-1, 0);
	ivec2 sy = texel.y < maxTexel.y ? ivec2(0, 1) : ivec2(0, -1);
	ivec2 t1 = texel + sx;
	ivec2 t2 = texel + sy;
	vec3 p1 = ScreenToViewSpace(vec3((vec2(t1) + 0.5) * screenPixelSize, texelFetch(depthtex0, t1, 0).x));
	vec3 p2 = ScreenToViewSpace(vec3((vec2(t2) + 0.5) * screenPixelSize, texelFetch(depthtex0, t2, 0).x));
	vec3 n = cross(p1 - viewPos, p2 - viewPos);
	float len = length(n);
	if (!(len > 1e-12)) return mat3(gbufferModelViewInverse) * normalize(-viewPos);
	n /= len;
	if (dot(n, viewPos) > 0.0) n = -n;
	return mat3(gbufferModelViewInverse) * n;
}

vec4 WaterReflection(in vec3 viewPos, in vec3 viewNormal, in float depth, in float skylight) {
	vec3 viewDir = normalize(viewPos);
	vec3 rayDir = reflect(viewDir, viewNormal);

	float NdotL = dot(viewNormal, rayDir);
	if (NdotL < 1e-6) return vec4(0.0, 0.0, 0.0, 1.0);

	float NdotV = max(1e-6, dot(viewNormal, -viewDir));
	float dither = InterleavedGradientNoise(gl_FragCoord.xy);

	vec3 screenPos = vec3(gl_FragCoord.xy * screenPixelSize, depth);
	bool hit = ScreenSpaceRayTrace(viewPos, rayDir, dither, RAYTRACE_SAMPLES, screenPos);

	vec3 rayDirWorld = mat3(gbufferModelViewInverse) * rayDir;
	vec3 reflection = vec3(0.0);

	if (hit) {
		reflection = texelFetch(deferredTex, ivec2(screenPos.xy), 0).rgb;
	} else if (isEyeInWater == 0) {
		if (skylight > 1e-3) {
			float NdotU = saturate((dot(viewNormal, gbufferModelView[1].xyz) + 0.7) * 2.0) * 0.75 + 0.25;
			vec4 clouds = SampleCloudDome(rayDirWorld);
			vec3 sky = SampleSkyRadiance(rayDirWorld) * clouds.a + clouds.rgb;
			reflection = sky * skylight * NdotU;
			// 只反射地平线以上的太阳（夜里太阳在地下，向下的反射光线不能照到它）
			float aboveHorizon = smoothstep(-0.01, 0.02, rayDirWorld.y);
			reflection += RenderSunReflection(rayDirWorld, worldSunVector) * clouds.a * skylight * GetTransmittance(rayDirWorld) * aboveHorizon;
		}
	} else {
		reflection = vec3(0.05, 0.7, 1.0) * 0.25 * (timeNoon + timeMidnight * NIGHT_BRIGHTNESS);
	}

	float specular = isEyeInWater == 1
		? FresnelDielectricN(NdotV, 1.0 / WATER_REFRACT_IOR)
		: FresnelDielectricN(NdotV, WATER_REFRACT_IOR);

	return clamp16F(vec4(reflection * specular, 1.0 - specular));
}

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy);
	vec2 screenCoord = gl_FragCoord.xy * screenPixelSize;

	float depth = texelFetch(depthtex0, texel, 0).x;
	float depthSoild = texelFetch(depthtex1, texel, 0).x;

	vec3 viewPos = ScreenToViewSpace(vec3(screenCoord, depth));
	vec3 worldPos = mat3(gbufferModelViewInverse) * viewPos;
	vec3 worldDir = normalize(worldPos);

	vec3 color = texelFetch(deferredTex, texel, 0).rgb;

	bool isWater = depth < depthSoild;
	if (isWater) {
		vec3 viewPosSoild = ScreenToViewSpace(vec3(screenCoord, depthSoild));
		vec3 absolutePos = worldPos + cameraPosition;
		vec3 geoNormal = ReconstructWorldNormal(texel, viewPos);

		// 顶面用 Derivative 的波浪法线，侧面（瀑布等）用几何法线
		vec3 worldNormal = geoNormal;
		if (abs(geoNormal.y) > 0.7) {
			vec3 wavesNormal = GetWavesNormal(absolutePos.xz - absolutePos.y).xzy;
			worldNormal = wavesNormal * sign(geoNormal.y);
		}
		vec3 viewNormal = mat3(gbufferModelView) * worldNormal;

		// 折射（Derivative 非光追版本；屏幕 y 向下，所以垂直偏移取反）
		float waterDepthLinear = GetDepthLinear(depth);
		float refractionDepth = GetDepthLinear(depthSoild) - waterDepthLinear;
		vec3 nv = normalize(gbufferModelView[1].xyz);
		vec2 refractOffset = nv.xy - normalize(viewNormal).xy;
		refractOffset *= saturate(refractionDepth) * 0.5 / (waterDepthLinear + 1e-4);
		vec2 refractCoord = screenCoord + vec2(refractOffset.x, -refractOffset.y);
		refractCoord = saturate(refractCoord);
		ivec2 refractTexel = ivec2(refractCoord * screenSize);
		if (texelFetch(depthtex1, refractTexel, 0).x < depth) refractTexel = texel;

		color = texelFetch(deferredTex, refractTexel, 0).rgb;

		// 水中的雾（光被水吸收、散射）
		float skylight = GetSkyExposure(absolutePos + vec3(0.0, 0.5, 0.0));
		if (isEyeInWater == 0) {
			float LdotV = dot(worldLightVector, worldDir);
			WaterFog(color, max(skylight, 0.15), LdotV, distance(viewPos, viewPosSoild));
		}

		vec4 reflectionData = WaterReflection(viewPos, normalize(viewNormal), depth, skylight);
		color = color * reflectionData.a + reflectionData.rgb;
	}

	float fogDist = length(viewPos);

	if (isEyeInWater == 1) UnderwaterFog(color, fogDist);
	if (isEyeInWater == 0 && depth < 1.0) {
		// https://github.com/zombye/spectrum （Derivative LAND_ATMOSPHERIC_SCATTERING）
		const float airNumberDensity       = 2.5035422e25;
		const float ozoneConcentrationPeak = 4e-6;
		const float ozoneNumberDensity     = airNumberDensity * 0.012578 * (134.628 / 48.0) * ozoneConcentrationPeak; // exp(-35e3 / 8e3) = 0.012578
		const vec3  ozoneCrossSection      = vec3(4.51103766177301E-21, 3.2854797958699E-21, 1.96774621921165E-22) * 0.0001;

		const vec3  rayleighColor = vec3(6.433377384678407e+24, 1.0873673940138444e+25, 2.4861429602679963e+25);
		const float rayleighK     = 9.993284137187039e-31;

		const vec3 atmosphere_coefficientRayleigh = rayleighK * rayleighColor;
		const vec3 atmosphere_coefficientOzone    = ozoneCrossSection * ozoneNumberDensity;
		const vec3 atmosphere_coefficientMie      = vec3(4e-6);

		const vec3 baseAttenuationCoefficient = atmosphere_coefficientRayleigh + atmosphere_coefficientMie + atmosphere_coefficientOzone;

		float dist = fogDist * eyeSkylightFix * 20.0;

		vec3 opticalDepth = baseAttenuationCoefficient * dist;
		vec3 transmittance   = exp(-opticalDepth);
		vec3 visibleFraction = min(oneMinus(transmittance) / max(opticalDepth, vec3(1e-9)), 1.0);

		float LdotV = dot(worldLightVector, worldDir);

		vec3 scattering = atmosphere_coefficientRayleigh * RayleighPhase(LdotV) * directIlluminance
		                + atmosphere_coefficientMie * 0.9 * HenyeyGreensteinPhase(LdotV, mie_phase_g) * directIlluminance;
		scattering += (atmosphere_coefficientRayleigh + atmosphere_coefficientMie * 0.9) * (0.25 * rPI) * skyIlluminance;
		scattering *= dist * visibleFraction;

		color = color * transmittance + scattering * 20.0 * oneMinus(wetness * 0.6);

		// 边界雾：地形尽头融进天空
		float density = 1.0 - exp2(-sqr(pow4(length(worldPos.xz) * eyeSkylightFix / renderDistance)) * BORDER_FOG_FALLOFF);
		density *= oneMinus(saturate(worldDir.y * 3.0));
		color = mix(color, SampleSkyWithClouds(worldDir), density);
	}

	vec4 fogData = SpatialUpscale(gl_FragCoord.xy, GetDepthLinear(depth));
	color = color * fogData.a + fogData.rgb;

	sceneOut = vec4(SanitizeHdr(color), 1.0);
}
