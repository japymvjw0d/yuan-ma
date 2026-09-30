// ShaderMod v2：曝光。
// Derivative 用整帧平均亮度做自动曝光；这里改为由天空光照强度直接推算"场景平均亮度"，
// 再套用 Derivative 的曝光曲线（K = 19）。好处是不需要回读 GPU，且昼夜过渡平滑、稳定。

uniform float exposureBias;      // 用户曝光补偿（EV）
uniform float exposureSceneScale; // 光照强度 → 平均亮度的换算系数

float CalculateExposure(in vec3 directIllum, in vec3 skyIllum) {
	float avgLum = (GetLuminance(directIllum) * 1.6 + GetLuminance(skyIllum) * 2.2) * exposureSceneScale;
	avgLum = pow(max(avgLum, 1e-8), 0.75);

	const float K = 19.0;
	const float calibration = K * 1e-2;
	const float a = K * 1e-2 * 18.0;
	const float b = a - K * 1e-2 * 0.04;

	return exp2(exposureBias) * calibration / (a - b * fastExp(-avgLum * rcp(b)));
}
