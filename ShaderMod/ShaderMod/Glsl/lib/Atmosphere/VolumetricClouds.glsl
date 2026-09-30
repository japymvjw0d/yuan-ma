/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
*/


#define CLOUD_CUMULUS_CLEAR_ALTITUDE	1000.0 // [60 80 100 150 200 300 356 400 500 600 700 800 1000 1200 1500 2000 5000 10000]

#define CLOUD_CUMULUS_CLEAR_THICKNESS	1400.0 // [0 100 200 300 400 500 550 600 700 800 1000 1200 1400 1500 1800 2000 3000 5000 6000]

#define CLOUD_CUMULUS_CLEAR_COVERY		1.0  // [0.5 0.7 0.8 0.9 1.0 1.1 1.15 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.1 2.3 2.5 2.7 3.0]

#define CLOUD_CUMULUS_CLEAR_DENSITY 	1.0  // [0.0 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 5.0]

#define CLOUD_CUMULUS_CLEAR_SUNLIGHTING	1.0  // [0.1 0.3 0.35 0.4 0.45 0.5 0.7 0.9 1.0 1.1 1.3 1.5 1.7 1.9 2.1 2.3 2.5]

#define CLOUD_CUMULUS_CLEAR_SKYLIGHTING	1.0	 // [0.0 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 5.0]


#define CLOUD_CUMULUS_RAIN_ALTITUDE		800.0  // [60 80 100 150 200 300 356 400 500 600 700 800 1000 1200 1500 2000 5000 10000]

#define CLOUD_CUMULUS_RAIN_THICKNESS	3000.0 // [0 100 200 300 400 465 500 600 800 1000 1200 1500 2000 2500 3000 3500 5000 6000 7000]

#define CLOUD_CUMULUS_RAIN_COVERY		1.2  // [0.5 0.7 0.8 0.9 1.0 1.1 1.15 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.1 2.3 2.5 2.7 3.0]

#define CLOUD_CUMULUS_RAIN_DENSITY 		1.0  // [0.0 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 5.0]

#define CLOUD_CUMULUS_RAIN_SUNLIGHTING	0.3  // [0.1 0.15 0.2 0.3 0.5 0.7 0.9 1.1 1.3 1.5 1.7 1.9 2.1 2.3 2.5]

#define CLOUD_CUMULUS_RAIN_SKYLIGHTING	0.3	 // [0.0 0.1 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9 1.0 1.1 1.2 1.3 1.4 1.5 1.6 1.7 1.8 1.9 2.0 2.5 3.0 5.0]



#define CLOUD_CUMULUS_SUNLIGHT_SAMPLES 	4u
#define CLOUD_CUMULUS_SKYLIGHT_SAMPLES 	2u

#define CLOUD_LOCAL_COVERAGE


//------------------------------------------------------------------------------------------------//

struct CloudProperties {
	float altitude;
	float thickness;
	float coverage;
	float density;
	float sunlighting, skylighting;
	float maxAltitude;
	float noiseScale;
	float cloudPeakWeight;
};

#ifdef CLOUDS_WEATHER
	flat in vec3 cloudDynamicWeather;
#endif

CloudProperties GetGlobalCloudProperties() {
	CloudProperties cloudProperties;

	#ifdef CLOUDS_WEATHER
		if (cloudDynamicWeather.z > 5e-3) {
			cloudProperties.altitude    = mix(CLOUD_CUMULUS_CLEAR_ALTITUDE,	   CLOUD_CUMULUS_RAIN_ALTITUDE,	   wetness) * (1.0 + cloudDynamicWeather.z * 2.0);
			cloudProperties.density     = mix(CLOUD_CUMULUS_CLEAR_DENSITY,	   CLOUD_CUMULUS_RAIN_DENSITY,	   wetness) * oneMinus(cloudDynamicWeather.z * 0.3);
			cloudProperties.sunlighting = mix(CLOUD_CUMULUS_CLEAR_SUNLIGHTING, CLOUD_CUMULUS_RAIN_SUNLIGHTING, wetness) * (1.0 + cloudDynamicWeather.z * 0.2);
			cloudProperties.skylighting = mix(CLOUD_CUMULUS_CLEAR_SKYLIGHTING, CLOUD_CUMULUS_RAIN_SKYLIGHTING, wetness) * (1.0 + cloudDynamicWeather.z * 0.2);
		} else
	#endif
	{
		cloudProperties.altitude    = mix(CLOUD_CUMULUS_CLEAR_ALTITUDE,	   CLOUD_CUMULUS_RAIN_ALTITUDE,	   wetness);
		cloudProperties.density     = mix(CLOUD_CUMULUS_CLEAR_DENSITY,	   CLOUD_CUMULUS_RAIN_DENSITY,	   wetness);
		cloudProperties.sunlighting = mix(CLOUD_CUMULUS_CLEAR_SUNLIGHTING, CLOUD_CUMULUS_RAIN_SUNLIGHTING, wetness);
		cloudProperties.skylighting = mix(CLOUD_CUMULUS_CLEAR_SKYLIGHTING, CLOUD_CUMULUS_RAIN_SKYLIGHTING, wetness);
	}
	cloudProperties.thickness   = mix(CLOUD_CUMULUS_CLEAR_THICKNESS,   CLOUD_CUMULUS_RAIN_THICKNESS,   wetness);
	cloudProperties.coverage    = mix(CLOUD_CUMULUS_CLEAR_COVERY,	   CLOUD_CUMULUS_RAIN_COVERY,	   wetness);
	cloudProperties.maxAltitude	= cloudProperties.altitude + cloudProperties.thickness;
	cloudProperties.noiseScale 	= 4e-4 + 6e-5 * wetness;
	cloudProperties.cloudPeakWeight = 0.1 + 0.7 * wetness;

	return cloudProperties;
}

//------------------------------------------------------------------------------------------------//


// uniform sampler3D colortex8;

#define wind (vec3(2e-3, 2e-4, 1e-3) * worldTimeCounter * CLOUDS_SPEED)

#define cloudForwardG (0.6 - wetness * 0.2)
#define cloudBackwardG (-0.4 + wetness * 0.2)
const float cloudBackwardWeight = 0.25, octWeight = 0.5, octScale = 3.0;

float CloudVolumeDensity(in CloudProperties cloudProperties, in vec3 worldPos, in uint steps, in float noiseDetail) {
	#ifdef CLOUD_LOCAL_COVERAGE
		float localCoverage = texture(noisetex, worldPos.xz * 2e-7 - wind.xz * 2e-3).y;
		localCoverage = saturate(fma(localCoverage, 3.0, wetness - 0.4)) * 0.5 + 0.5;
		if (localCoverage < 0.1) return 0.0;
    #endif

	vec3 position = worldPos * cloudProperties.noiseScale - wind;

	float density = noiseDetail * 0.03, weight = 0.5;

    for (uint i = 0u; i < steps; ++i, weight *= octWeight) {
		density += weight * Get3DNoiseSmooth(position);
        position = position * octScale - wind;
    }

	density += octWeight / octScale / float(steps);

	// vec4 lowFreqNoises = texture(colortex8, fract(position));
	// float fbm = lowFreqNoises.g * 0.625 + lowFreqNoises.b * 0.25 + lowFreqNoises.a * 0.125;

	// float density = saturate(remap(fbm - 1.0, 1.0, lowFreqNoises.x));

	if (density < 1e-6) return 0.0;

    #ifdef CLOUD_LOCAL_COVERAGE
		density *= localCoverage;
    #endif

	float normalizedHeight  = saturate((worldPos.y - cloudProperties.altitude) * rcp(cloudProperties.thickness));
	float heightAttenuation = saturate(normalizedHeight * 6.6) * saturate(oneMinus(normalizedHeight) * (2.0 + wetness));

	density = cloudProperties.coverage == 1.0 ? density : saturate((density - 1.0 + cloudProperties.coverage) * rcp(cloudProperties.coverage));

	density *= heightAttenuation * 1.9;
	density -= heightAttenuation * 0.9 + normalizedHeight * 0.5 + 0.1;

	return saturate(density * 3.0 * cloudProperties.density);
}

#ifdef CLOUDS_SHADOW
	float GetShadow3DNoiseSmooth(in vec3 position) {
		vec3 p = floor(position);
		vec3 b = curve(position - p);

		ivec2 texel = ivec2(p.xy + 97.0 * p.z);

		vec2 s0 = texelFetch(noisetex, texel & (noiseTextureResolution - 1), 0).xy;
		vec2 s1 = texelFetch(noisetex, (texel + ivec2(1, 0)) & (noiseTextureResolution - 1), 0).xy;
		vec2 s2 = texelFetch(noisetex, (texel + ivec2(0, 1)) & (noiseTextureResolution - 1), 0).xy;
		vec2 s3 = texelFetch(noisetex, (texel + ivec2(1, 1)) & (noiseTextureResolution - 1), 0).xy;

		vec2 rg = mix(mix(s0, s1, b.x), mix(s2, s3, b.x), b.y);

		return mix(rg.x, rg.y, b.z);
	}

	float CloudVolumeDensitySmooth(in CloudProperties cloudProperties, in vec3 worldPos) {
		#ifdef CLOUD_LOCAL_COVERAGE
			float localCoverage = texture(noisetex, worldPos.xz * 2e-7 - wind.xz * 2e-3 + 0.5).y;
			localCoverage = saturate(fma(localCoverage, 3.0, wetness - 0.4)) * 0.5 + 0.5;
			if (localCoverage < 0.1) return 0.0;
		#endif

		vec3 position = worldPos * cloudProperties.noiseScale - wind;

		float density = 0.03, weight = 0.5;

		for (uint i = 0u; i < 4u; ++i, weight *= octWeight) {
			density += weight * GetShadow3DNoiseSmooth(position);
			position = (position - wind) * octScale;
		}

		density += octWeight / octScale * 0.25;

		if (density < 1e-6) return 0.0;

		#ifdef CLOUD_LOCAL_COVERAGE
			density *= localCoverage;
		#endif

		float normalizedHeight  = saturate((worldPos.y - cloudProperties.altitude) * rcp(cloudProperties.thickness));
		float heightAttenuation = saturate(normalizedHeight * 6.6) * saturate(oneMinus(normalizedHeight) * (2.0 + wetness));

		density = cloudProperties.coverage == 1.0 ? density : saturate((density - 1.0 + cloudProperties.coverage) * rcp(cloudProperties.coverage));

		density *= heightAttenuation * 1.9;
		density -= heightAttenuation * 0.9 + normalizedHeight * 0.5 + 0.1;

		return saturate(density * 3.0 * cloudProperties.density);
	}
#endif

float GetNoiseDetail(in vec3 worldDir) {
	//worldDir = worldDir * 5.0 - wind;
	worldDir *= 48.0;

	//float pnoise = 	texture3D(colortex4, fract(worldDir)).z; 		 		worldDir += pnoise * 1e-2 - wind;
	//pnoise +=  		texture3D(colortex4, fract(worldDir * 2.0)).z * 0.5;	worldDir += pnoise * 1e-2 - wind;
	//pnoise +=  		texture3D(colortex4, fract(worldDir * 4.0)).z * 0.25;	worldDir += pnoise * 1e-3 - wind;
	//pnoise +=  		texture3D(colortex4, fract(worldDir * 8.0)).z * 0.125;
	float pnoise = 	Get3DNoise(worldDir - wind); 		 worldDir += pnoise * 1e-3 - wind;
	pnoise +=  		Get3DNoise(worldDir * 2.0);			 worldDir += pnoise * 1e-3 - wind;
	pnoise +=  		Get3DNoise(worldDir * 4.0) * 0.5;	 worldDir += pnoise * 1e-3 - wind;
	pnoise +=  		Get3DNoise(worldDir * 8.0) * 0.25;	 worldDir += pnoise * 1e-3 - wind;
	pnoise +=  		Get3DNoise(worldDir * 16.0) * 0.125; worldDir += pnoise * 1e-3 - wind;

	//return pnoise * 1.2 - 0.1;
	return pnoise - 0.15;
}

//vec3 cumulusSunlightColor = CumulusSunlightColor();

float CloudVolumeSunLightOD(in CloudProperties cloudProperties, in vec3 rayPos, in float lightNoise) {
    float rayLength = cloudProperties.thickness * (0.2 / float(CLOUD_CUMULUS_SUNLIGHT_SAMPLES));
	vec4 rayStep = vec4(worldLightVector, 1.0) * rayLength;

    float opticalDepth = 0.0;

	for (uint i = 0u; i < CLOUD_CUMULUS_SUNLIGHT_SAMPLES; ++i, rayPos += rayStep.xyz) {
        rayStep *= 2.0;
		// if (rayPos.y < cloudProperties.altitude || rayPos.y > cloudProperties.maxAltitude) continue;

		float density = CloudVolumeDensity(cloudProperties, rayPos + rayStep.xyz * lightNoise, 5u, 1.0);
		if (density < 1e-4) continue;

        // opticalDepth += density * rayStep.w;
        opticalDepth += density;
    }

    return opticalDepth * rayLength * 0.12;
}

float CloudVolumeSkyLightOD(in CloudProperties cloudProperties, in vec3 rayPos, in float lightNoise) {
    float rayLength = cloudProperties.thickness * (0.2 / float(CLOUD_CUMULUS_SKYLIGHT_SAMPLES));
	vec4 rayStep = vec4(vec3(0.0, 1.0, 0.0), 1.0) * rayLength;

    float opticalDepth = 0.0;

	for (uint i = 0u; i < CLOUD_CUMULUS_SKYLIGHT_SAMPLES; ++i, rayPos += rayStep.xyz) {
        rayStep *= 2.0;
		// if (rayPos.y < cloudProperties.altitude || rayPos.y > cloudProperties.maxAltitude) continue;

		float density = CloudVolumeDensity(cloudProperties, rayPos + rayStep.xyz * lightNoise, 3u, 1.0);
		if (density < 1e-4) continue;

        // opticalDepth += density * rayStep.w;
        opticalDepth += density;
    }

    // return opticalDepth * 0.04;
    return opticalDepth * rayLength * 0.04;
}

