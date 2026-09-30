// ShaderMod v2：屏幕/视图/世界坐标变换（对应 Derivative lib/Head/Functions.inc，按生存战争的矩阵约定重写）

// screenPos.xy ∈ [0,1]，y=0 为画面顶部；screenPos.z 为深度纹理值
vec3 ScreenToViewSpace(in vec3 screenPos) {
	vec4 viewPos = gbufferProjectionInverse * vec4(screenPos.x * 2.0 - 1.0, 1.0 - screenPos.y * 2.0, screenPos.z, 1.0);
	return viewPos.xyz / viewPos.w;
}

vec3 ViewToScreenSpace(in vec3 viewPos) {
	vec4 clipPos = gbufferProjection * vec4(viewPos, 1.0);
	vec3 ndc = clipPos.xyz / clipPos.w;
	return vec3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, ndc.z);
}

float GetDepthLinear(in float depth) {
	return (near * far) / (depth * (near - far) + far);
}

// 视线方向（世界空间，未归一化也可）
vec3 ScreenToWorldDir(in vec2 screenCoord) {
	vec3 viewPos = ScreenToViewSpace(vec3(screenCoord, 1.0));
	return normalize(mat3(gbufferModelViewInverse) * viewPos);
}
