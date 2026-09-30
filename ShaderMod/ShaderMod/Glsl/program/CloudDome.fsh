// ShaderMod v2：云穹图（对应 Derivative program/Deferred0.glsl 的 RenderSkybox 云层部分）。
// 输出 2048×512 等距柱状图，只覆盖上半球：x = 方位角，y = 仰角（0 = 地平线，1 = 天顶）。
// rgb = 云的散射光，a = 透射率。每帧只更新其中一条（由 C# 用剪裁矩形控制），8 帧刷新一遍。

#define PRECOMPUTED_ATMOSPHERIC_SCATTERING

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"

uniform sampler2D noisetex;
uniform sampler2D skyMap;
uniform sampler3D atmosphereLut;
uniform vec2 domeSize;

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;

#ifndef CLOUD_CUMULUS_SAMPLES
	#define CLOUD_CUMULUS_SAMPLES 32u
#endif

#include "/lib/Head/Noise.inc"
#include "/lib/Atmosphere/Atmosphere.glsl"
#include "/lib/Atmosphere/VolumetricClouds.glsl"
#include "/lib/Atmosphere/PlanarClouds.glsl"

layout(location = 0) out vec4 cloudOut;

vec3 DomeToWorldDir(in vec2 coord) {
	float azimuth = (coord.x - 0.5) * TAU;
	float elevation = coord.y * (0.5 * PI);
	return vec3(sin(azimuth) * cos(elevation), sin(elevation), cos(azimuth) * cos(elevation));
}

void GetPlanetCurvePosition(inout vec3 p) {
	p.y = length(p + vec3(0.0, planetRadius, 0.0)) - planetRadius;
}

vec4 RenderClouds(in vec3 worldDir, in CloudProperties cloudProperties) {
	vec4 cloudsData = vec4(0.0, 0.0, 0.0, 1.0);

	vec3 skyRadiance = textureLod(skyMap, ProjectSky(worldDir), 0.0).rgb;

	float LdotV = dot(worldDir, worldLightVector);

	vec4 phases;	/* forwardsLobe */										/* backwardsLobe */																	/* forwardsPeak */
	phases.x = 	HenyeyGreensteinPhase(LdotV, cloudForwardG) 	  * 0.7  + HenyeyGreensteinPhase(LdotV, cloudBackwardG)		  * cloudBackwardWeight  	  + CornetteShanksPhase(LdotV, 0.9) * cloudProperties.cloudPeakWeight;
	phases.y = 	HenyeyGreensteinPhase(LdotV, cloudForwardG * 0.7) * 0.35 + HenyeyGreensteinPhase(LdotV, cloudBackwardG * 0.7) * cloudBackwardWeight * 0.6 + CornetteShanksPhase(LdotV, 0.6) * cloudProperties.cloudPeakWeight * 0.5;
	phases.z = 	HenyeyGreensteinPhase(LdotV, cloudForwardG * 0.5) * 0.17 + HenyeyGreensteinPhase(LdotV, cloudBackwardG * 0.5) * cloudBackwardWeight * 0.3 + CornetteShanksPhase(LdotV, 0.4) * cloudProperties.cloudPeakWeight * 0.2;
	phases.w = 	HenyeyGreensteinPhase(LdotV, cloudForwardG * 0.3) * 0.08 + HenyeyGreensteinPhase(LdotV, cloudBackwardG * 0.3) * cloudBackwardWeight * 0.2 + CornetteShanksPhase(LdotV, 0.2) * cloudProperties.cloudPeakWeight * 0.1;

	vec3 planeOrigin = vec3(0.0, planetRadius + eyeAltitude, 0.0);

	#ifdef VOLUMETRIC_CLOUDS
		if (worldDir.y > 0.0 && eyeAltitude < cloudProperties.altitude) {
			vec2 bottomIntersection = RaySphereIntersection(planeOrigin, worldDir, planetRadius + cloudProperties.altitude);
			vec2 topIntersection = RaySphereIntersection(planeOrigin, worldDir, planetRadius + cloudProperties.maxAltitude);

			float startLength = bottomIntersection.y;
			float endLength = topIntersection.y;

			uint raySteps = CLOUD_CUMULUS_SAMPLES;
			raySteps = uint(mix(float(raySteps), float(raySteps) * 0.6, abs(worldDir.y))); // Steps Fade

			float rayLength = clamp(endLength - startLength, 0.0, 2e4) * rcp(float(raySteps));
			vec3 rayStep = rayLength * worldDir;
			GetPlanetCurvePosition(rayStep);
			vec3 rayPos = (startLength + rayLength * 0.5) * worldDir + cameraPosition;
			GetPlanetCurvePosition(rayPos);

			float noiseDetail = GetNoiseDetail(worldDir);

			float scatteringSun = 0.0;
			float scatteringSky = 0.0;

			float transmittance = 1.0;

			for (uint i = 0u; i < raySteps; ++i, rayPos += rayStep) {
				if (transmittance < minTransmittance) break;
				if (rayPos.y < cloudProperties.altitude || rayPos.y > cloudProperties.maxAltitude) continue;

				float dist = distance(rayPos, cameraPosition);
				if (dist > planetRadius + cloudProperties.maxAltitude) continue;

				float density = CloudVolumeDensity(cloudProperties, rayPos, 5u, mix(noiseDetail, 1.0, exp2(-dist * 0.001)));
				if (density < 1e-4) continue;

				float sunlightOD = CloudVolumeSunLightOD(cloudProperties, rayPos, 0.5);

				float powder = oneMinus(fastExp(-density * 36.0)) * 0.82;
				powder /= 1.0 - powder;

				float sunlightEnergy = 	fastExp(-sunlightOD * 2.0) * phases.x;
				sunlightEnergy += 		fastExp(-sunlightOD * 0.8) * phases.y;
				sunlightEnergy += 		fastExp(-sunlightOD * 0.3) * phases.z;
				sunlightEnergy += 		fastExp(-sunlightOD * 0.1) * phases.w;

				float skylightEnergy = CloudVolumeSkyLightOD(cloudProperties, rayPos, 0.5);
				skylightEnergy = fastExp(-skylightEnergy) + fastExp(-skylightEnergy * 0.1) * 0.1;

				float stepTransmittance = fastExp(-density * 0.12 * rayLength);
				float cloudsTemp = powder * transmittance * oneMinus(stepTransmittance);
				scatteringSun += sunlightEnergy * cloudsTemp;
				scatteringSky += skylightEnergy * cloudsTemp;
				transmittance *= stepTransmittance;
			}

			if (transmittance < 1.0 - minTransmittance) {
				bool moonlit = worldSunVector.y < -0.04;
				vec3 scattering = scatteringSun * 22.0 * cloudProperties.sunlighting * (moonlit ? moonIlluminance : sunIlluminance);
				scattering += scatteringSky * 0.15 * cloudProperties.skylighting * skyIlluminance;

				if (isLightningFlashing > 1e-2) scattering += sqr(scatteringSky) * 0.1 * lightningColor;

				rayPos -= cameraPosition;
				float atmosFade = fastExp(-length(rayPos) * (0.2 + 0.1 * wetness) * 1e-4);
				scattering = scattering * atmosFade + skyRadiance * oneMinus(transmittance) * oneMinus(atmosFade);

				cloudsData = vec4(scattering, transmittance);
			}
		}
	#endif

	#ifdef PLANAR_CLOUDS
		if (worldDir.y > 0.0 && eyeAltitude < CLOUD_PLANE_ALTITUDE) {
			vec2 intersection = RaySphereIntersection(planeOrigin, worldDir, planetRadius + CLOUD_PLANE_ALTITUDE);
			float cloudDistance = intersection.y;

			if (cloudDistance > 0.0 && cloudDistance < planetRadius + CLOUD_PLANE_ALTITUDE) {
				vec3 cloudPos = worldDir * cloudDistance + cameraPosition;

				vec4 cloudsTemp = vec4(0.0, 0.0, 0.0, 1.0);

				#ifdef CIRROCUMULUS_CLOUDS
				{
					vec4 sampleTemp = PlanarSample1(cloudDistance, cloudPos.xz, LdotV, 0.5, phases, worldDir);

					if (sampleTemp.a > minTransmittance) {
						float atmosFade = fastExp(-cloudDistance * fma(0.05, wetness, 0.1) * 0.00015);
						sampleTemp.rgb = sampleTemp.rgb * atmosFade + skyRadiance * sampleTemp.a * oneMinus(atmosFade);
					}

					cloudsTemp.rgb = sampleTemp.rgb;
					cloudsTemp.a -= sampleTemp.a;
				}
				#endif
				#if CIRRUS_CLOUDS > 0
				{
					vec4 sampleTemp = PlanarSample0(cloudDistance, cloudPos.xz, LdotV);

					if (sampleTemp.a > minTransmittance) {
						float atmosFade = fastExp(-cloudDistance * fma(0.05, wetness, 0.1) * 0.00015);
						sampleTemp.rgb = sampleTemp.rgb * atmosFade + skyRadiance * sampleTemp.a * oneMinus(atmosFade);
					}

					cloudsTemp.rgb += sampleTemp.rgb * cloudsTemp.a;
					cloudsTemp.a *= 1.0 - sampleTemp.a;
				}
				#endif

				cloudsData.rgb += cloudsTemp.rgb * cloudsData.a;
				cloudsData.a *= cloudsTemp.a;
			}
		}
	#endif

	return cloudsData;
}

void main() {
	vec2 coord = gl_FragCoord.xy / domeSize;
	vec3 worldDir = DomeToWorldDir(coord);

	vec4 clouds = RenderClouds(worldDir, GetGlobalCloudProperties());
	cloudOut = vec4(clamp16F(clouds.rgb), saturate(clouds.a));
}
