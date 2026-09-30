// ShaderMod v2：延迟光照（对应 Derivative world0/deferred5.fsh）。
// 生存战争的地形把"天光 + 方块光 + 面朝向明暗"烘焙在顶点颜色里，没有法线和光照贴图，
// 所以这里不重新计算完整光照，而是按 Derivative 的阳光/天光模型求出"此处相对于露天平地的光照比例"，
// 用它重新分配原版亮度：受光面更亮更暖，阴影里更暗更蓝；洞穴、室内（头顶有遮挡）保持原版亮度。
// 输出：绝对亮度的 HDR（与天空同一单位），不含水面（水面在 Composite 里处理）。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING
#define NEED_SKY_SH

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"
#include "/lib/Head/Functions.inc"

uniform sampler2D sceneTex;     // 游戏画好的场景（8 位 sRGB）
uniform sampler2D depthtex1;    // 场景深度（不含水面）
uniform sampler2D heightMap;
uniform sampler2D skyMap;
uniform sampler2D cloudDome;
uniform sampler2D noisetex;
uniform sampler2D shadowtex0;
uniform sampler2DShadow shadowtex1;
uniform sampler3D atmosphereLut;

uniform float vanillaGain;
uniform float lightingStrength;  // 0 = 原版亮度分布，1 = 完全按阳光/阴影重新分配
uniform float shadowLift;        // 阴影最暗处保留的比例
uniform int debugView;           // 调试：1 法线，2 阴影，3 露天程度，4 光照比例

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;
flat in vec4 skySHR;
flat in vec4 skySHG;
flat in vec4 skySHB;

#include "/lib/Head/Noise.inc"
#include "/lib/Atmosphere/Atmosphere.glsl"
#include "/lib/Head/SkyCommon.glsl"
#include "/lib/Lighting/SunLighting.glsl"

#ifdef CLOUDS_SHADOW
	#include "/lib/Atmosphere/VolumetricClouds.glsl"

	#define CLOUD_PLANE_ALTITUDE 7000.0
	#define CLOUD_PLANE1_COVERY 0.5

	float CloudPlanarDensity(in vec2 worldPos) {
		worldPos /= 1.0 + distance(worldPos, cameraPosition.xz) * 2e-5;
		vec2 position = worldPos * 1e-4 - wind.xz;

		float baseCoverage = curve(texture(noisetex, position * 0.08).z * 0.7 + 0.1);
		baseCoverage *= max0(1.07 - texture(noisetex, position * 0.003).y * 1.4);

		vec2 curl = texture(noisetex, position * 0.05).xy * 0.04;
		curl += texture(noisetex, position * 0.1).xy * 0.02;
		position += curl;
		float noise = 0.5 * texture(noisetex, position * vec2(0.4, 0.16)).z;
		noise += texture(noisetex, position * 0.9).z - 0.24;
		noise = saturate(noise);

		noise *= clamp((baseCoverage + CLOUD_PLANE1_COVERY - 0.6) * 0.9, 0.0, 0.14);
		if (noise < 1e-6) return 0.0;
		position.x += noise * 0.2;

		noise += 0.02 * texture(noisetex, position * 3.0).z;
		noise += 0.01 * texture(noisetex, position * 5.0 + curl).z - 0.05;

		return cube(saturate(noise * (4.0 + wetness)));
	}

	float CloudShadow(in vec3 worldPos, in CloudProperties cloudProperties) {
		float cloudDensity = 0.0;
		vec3 checkOrigin = worldPos + vec3(0.0, planetRadius, 0.0);

		float checkRadius = planetRadius + cloudProperties.altitude;
		vec3 checkPos = RaySphereIntersection(checkOrigin, worldLightVector, checkRadius + 0.15 * cloudProperties.thickness).y * worldLightVector + worldPos;
		cloudDensity += CloudVolumeDensitySmooth(cloudProperties, checkPos);

		checkPos = RaySphereIntersection(checkOrigin, worldLightVector, checkRadius + 0.5 * cloudProperties.thickness).y * worldLightVector + worldPos;
		cloudDensity += CloudVolumeDensitySmooth(cloudProperties, checkPos);

		vec2 checkPos1 = RaySphereIntersection(checkOrigin, worldLightVector, planetRadius + CLOUD_PLANE_ALTITUDE).y * worldLightVector.xz + worldPos.xz;
		cloudDensity += CloudPlanarDensity(checkPos1) * 10.0;

		cloudDensity = mix(0.4, cloudDensity, saturate(sqr(abs(worldLightVector.y) * 2.0)));
		cloudDensity = saturate(cloudDensity);

		return exp2(-cloudDensity * cloudDensity * 2e2);
	}
#endif

layout(location = 0) out vec4 sceneData;

vec3 GetViewPos(in ivec2 texel) {
	vec2 coord = (vec2(texel) + 0.5) * screenPixelSize;
	return ScreenToViewSpace(vec3(coord, texelFetch(depthtex1, texel, 0).x));
}

// 由深度重建法线：每个方向取深度变化较小的一侧，避免物体边缘出错
vec3 ReconstructViewNormal(in ivec2 texel, in vec3 viewPos) {
	ivec2 maxTexel = ivec2(screenSize) - 1;
	vec3 left  = GetViewPos(clamp(texel - ivec2(1, 0), ivec2(0), maxTexel));
	vec3 right = GetViewPos(clamp(texel + ivec2(1, 0), ivec2(0), maxTexel));
	vec3 up    = GetViewPos(clamp(texel - ivec2(0, 1), ivec2(0), maxTexel));
	vec3 down  = GetViewPos(clamp(texel + ivec2(0, 1), ivec2(0), maxTexel));

	vec3 dx = abs(right.z - viewPos.z) < abs(viewPos.z - left.z) ? right - viewPos : viewPos - left;
	vec3 dy = abs(down.z - viewPos.z) < abs(viewPos.z - up.z) ? down - viewPos : viewPos - up;

	vec3 normal = normalize(cross(dx, dy));
	if (dot(normal, viewPos) > 0.0) normal = -normal;
	return normal;
}

// 方块几乎都是轴对齐的面：接近坐标轴时吸附，去掉远处深度精度造成的噪点
vec3 SnapNormal(in vec3 n) {
	vec3 a = abs(n);
	float m = maxOf(a);
	if (m > 0.9) {
		if (a.x == m) return vec3(sign(n.x), 0.0, 0.0);
		if (a.y == m) return vec3(0.0, sign(n.y), 0.0);
		return vec3(0.0, 0.0, sign(n.z));
	}
	return n;
}

// 原版地形的朝向明暗系数：顶面 1.0，±z 面 0.84，±x 面 0.62，底面 0.5
float VanillaFaceLighting(in vec3 n) {
	return 0.5 + max(dot(n, vec3(0.12, 0.25, 0.34)), 0.0) + max(dot(n, vec3(-0.12, 0.25, -0.34)), 0.0);
}

// Derivative 的阳光 + 天光 + 反弹光（反照率取 0.3 的中性灰），单位与天空相同
vec3 DerivativeLighting(in vec3 worldNormal, in vec3 worldDir, in vec3 shadow) {
	vec3 sunlightMult = 64.0 * SUNLIGHT_INTENSITY * directIlluminance;

	float LdotV = dot(worldLightVector, -worldDir);
	float NdotV = saturate(dot(worldNormal, -worldDir));
	float NdotL = dot(worldNormal, worldLightVector);
	float halfwayNorm = inversesqrt(max(2.0 * LdotV + 2.0, 1e-6));
	float NdotH = (NdotL + NdotV) * halfwayNorm;

	vec3 diffuse = DiffuseHammon(LdotV, max(NdotV, 1e-2), NdotL, max(NdotH, 1e-2), 0.9, vec3(0.3));

	vec3 skylight = FromSH(skySHR, skySHG, skySHB, worldNormal);
	skylight *= worldNormal.y * 2.0 + 3.0;
	vec3 skySunLight = (worldNormal.y * 0.24 + 0.4) * directIlluminance;
	skylight = mix(skylight, skySunLight, wetness * 0.7);
	skylight = skylight * (0.8 - wetness * 0.2) + lightningColor * 1.2;

	vec3 lighting = max(skylight, vec3(0.0)) * SKYLIGHT_INTENSITY;
	lighting += CalculateFakeBouncedLight(worldNormal) * sunlightMult;
	lighting += shadow * diffuse * sunlightMult;
	return lighting;
}

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy);
	vec2 screenCoord = gl_FragCoord.xy * screenPixelSize;

	vec3 vanilla = SRGBtoLinear(texelFetch(sceneTex, texel, 0).rgb) * vanillaGain / exposure;

	float depth = texelFetch(depthtex1, texel, 0).x;
	vec3 viewPos = ScreenToViewSpace(vec3(screenCoord, depth));
	vec3 worldPos = mat3(gbufferModelViewInverse) * viewPos;
	vec3 worldDir = normalize(worldPos);

	if (depth >= 1.0) {
		// 天空：游戏已画好（SkyDraw），这里补上 HDR 的太阳
		vec4 clouds = SampleCloudDome(worldDir);
		vec3 sun = RenderSun(worldDir, worldSunVector) * GetTransmittance(worldDir) * smoothstep(-0.01, 0.01, worldDir.y);
		sceneData = vec4(clamp16F(vanilla + sun * remap(minTransmittance, 1.0, clouds.a)), 1.0);
		return;
	}

	vec3 viewNormal = ReconstructViewNormal(texel, viewPos);
	vec3 worldNormal = SnapNormal(mat3(gbufferModelViewInverse) * viewNormal);

	vec3 absolutePos = worldPos + cameraPosition;
	float skyExposure = GetSkyExposure(absolutePos + worldNormal * 0.5);

	vec3 relight = vec3(1.0);
	float strength = skyExposure * lightingStrength;
	// 高度图只覆盖相机周围，超出范围逐渐退回原版亮度
	float heightMapHalf = float(textureSize(heightMap, 0).x) * 0.5;
	strength *= 1.0 - smoothstep(heightMapHalf - 24.0, heightMapHalf - 4.0, maxOf(abs(worldPos.xz)));

	if (strength > 1e-3) {
		float NdotL = dot(worldNormal, worldLightVector);

		// 阴影（与 Derivative 相同：法线偏移 + 遮挡物搜索 + PCF 软阴影，远处淡出）
		vec3 shadow = vec3(0.0);
		if (NdotL > 1e-3) {
			float dither = InterleavedGradientNoise(gl_FragCoord.xy);
			float distortFactor;
			vec3 normalOffset = worldNormal * (dotSelf(worldPos) * 8e-5 + 3e-2) * (2.0 - saturate(NdotL));
			vec3 shadowProjPos = WorldPosToShadowProjPosBias(worldPos + normalOffset, distortFactor);

			float distanceFade = saturate(pow16(rcp(shadowDistance * shadowDistance) * dotSelf(worldPos)));

			if (distanceFade < 1.0) {
				vec2 blockerSearch = BlockerSearch(shadowProjPos, dither);
				float penumbraScale = max(blockerSearch.x / distortFactor, 2.0 / realShadowMapRes);
				shadow = PercentageCloserFilter(shadowProjPos, dither, penumbraScale);
			}
			shadow = mix(shadow, vec3(1.0), distanceFade);

			#ifdef CLOUDS_SHADOW
				shadow *= max(CloudShadow(absolutePos, GetGlobalCloudProperties()), 0.03);
			#else
				shadow *= mix(1.0, 0.03, wetness);
			#endif
		}

		vec3 lighting = DerivativeLighting(worldNormal, worldDir, shadow);
		// 参照：朝上、无遮挡的露天平面（原版里这样的面亮度为满）
		vec3 reference = DerivativeLighting(vec3(0.0, 1.0, 0.0), vec3(0.0, -1.0, 0.0), vec3(1.0));
		vec3 ratio = lighting / max(GetLuminance(reference), 1e-7);
		ratio = max(ratio, vec3(shadowLift) * ratio / max(GetLuminance(ratio), 1e-4));
		// 原版已按朝向把侧面、底面压暗（LightingManager.CalculateLighting），先除掉，改由阳光/天光决定明暗
		ratio /= VanillaFaceLighting(worldNormal);

		relight = mix(vec3(1.0), ratio, strength);

		if (debugView == 2) { sceneData = vec4(shadow, 1.0); return; }
		if (debugView == 4) { sceneData = vec4(ratio * 0.5, 1.0); return; }
	}

	if (debugView == 1) { sceneData = vec4(worldNormal * 0.5 + 0.5, 1.0); return; }
	if (debugView == 3) { sceneData = vec4(vec3(skyExposure), 1.0); return; }

	sceneData = vec4(clamp16F(vanilla * relight), 1.0);
}
