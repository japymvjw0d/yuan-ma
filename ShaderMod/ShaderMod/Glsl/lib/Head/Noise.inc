// 移植自 Derivative lib/Head/Noise.inc（去掉了依赖全局初始化的随机状态）

const int noiseTextureResolution = 256;
const float noiseTexturePixelSize = 1.0 / float(noiseTextureResolution);

float Get3DNoise(in vec3 position) {
	vec3 p = floor(position);
	vec3 f = saturate(position - p);

	vec2 uv = p.xy + f.xy + p.z * 97.0;

	vec2 coord = (uv + 0.5) * noiseTexturePixelSize;
	vec2 noiseSample = texture(noisetex, coord).xy;
	return mix(noiseSample.x, noiseSample.y, f.z);
}

float bayer2(vec2 a) {
	a = floor(a);
	return fract(dot(a, vec2(0.5, a.y * 0.75)));
}

#define bayer4(a)   (bayer2(  0.5 * (a)) * 0.25 + bayer2(a))
#define bayer8(a)   (bayer4(  0.5 * (a)) * 0.25 + bayer2(a))
#define bayer16(a)  (bayer8(  0.5 * (a)) * 0.25 + bayer2(a))

float Get3DNoiseSmooth(in vec3 position) {
	vec3 p = floor(position);
	vec3 b = curve(position - p);

	vec2 uv = p.xy + b.xy + 97.0 * p.z;
	vec2 rg = texture(noisetex, (uv + 0.5) * noiseTexturePixelSize).xy;

	return mix(rg.x, rg.y, b.z);
}

const float PHI1 = 1.61803398874;
const float PHI2 = 1.32471795724;

float R1(in int n, in float seed) {
	const float alpha = 1.0 / PHI1;
	return fract(seed + float(n) * alpha);
}

float BlueNoiseTemporal() {
	return R1(frameCounter, texelFetch(noisetex, ivec2(gl_FragCoord.xy) & 255, 0).a);
}

float InterleavedGradientNoise(in vec2 coord) {
	return fract(52.9829189 * fract(0.06711056 * coord.x + 0.00583715 * coord.y));
}

float InterleavedGradientNoiseTemporal(in vec2 coord) {
	return fract(52.9829189 * fract(0.06711056 * coord.x + 0.00583715 * coord.y + 0.00623715 * float(frameCounter & 63)));
}
