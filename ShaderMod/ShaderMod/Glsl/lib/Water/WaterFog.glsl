/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。去掉了冰的分支（只处理水）。
	需要调用方提供 directIlluminance、skyIlluminance。
*/

void WaterFog(inout vec3 color, in float waterSkylight, in float LdotV, in float waterDepth) {
	float fogDensity = WATER_FOG_DENSITY * fma(0.1, wetnessCustom * eyeSkylightFix, 0.16) * waterDepth;

	vec3 waterFogColor = mix(skyIlluminance * 0.4, vec3(GetLuminance(skyIlluminance) * 0.1), 0.8 * wetnessCustom * eyeSkylightFix) * rPI;
	float scatter = HenyeyGreensteinPhase(LdotV, 0.65) + 0.1 * rPI;
	waterFogColor *= 1.0 + 28.0 * oneMinus(wetnessCustom * 0.8) * directIlluminance * scatter;

	vec3 transmittance = fastExp(-(waterAbsorption * 8.0 + 0.03) * fogDensity);

	color *= transmittance;
	color += waterFogColor * waterSkylight * oneMinus(transmittance);
}

void UnderwaterFog(inout vec3 color, in float waterDepth) {
	float fogDensity = WATER_FOG_DENSITY * fma(0.05, wetnessCustom * eyeSkylightFix, 0.1) * waterDepth;

	vec3 waterFogColor = mix(skyIlluminance * 0.4, vec3(GetLuminance(skyIlluminance) * 0.1), 0.8 * wetnessCustom * eyeSkylightFix) * rPI;

	vec3 transmittance = fastExp(-(waterAbsorption * 8.0 + 0.03) * max(fogDensity, 2.0) + 0.4);

	color *= transmittance;
	color += waterFogColor * saturate(eyeSkylightFix + 0.2) * oneMinus(transmittance);
}
