#ifdef GLSL

// ShaderMod：地形（不透明 / 镂空）顶点着色器。
// 在原版基础上额外输出相对坐标和指向相机的向量，供片元着色器重建法线、计算阳光。
// 原版的全部 uniform 与雾计算保持不变（TerrainRenderer 以不可为空的方式读取 u_fogBottomTopDensity、u_hazeStartDensity）。

// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='COLOR' Attribute='a_color' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

uniform vec2 u_origin;
uniform mat4 u_viewProjectionMatrix;
uniform vec3 u_viewPosition;
uniform float u_fogYMultiplier;
uniform vec3 u_fogBottomTopDensity;
uniform vec2 u_hazeStartDensity;

attribute vec3 a_position;
attribute vec4 a_color;
attribute vec2 a_texcoord;

varying vec4 v_color;
varying vec2 v_texcoord;
varying float v_fog;
varying vec3 v_relativePosition;
varying vec3 v_toCamera;

float fogIntegral(float y)
{
	return smoothstep(u_fogBottomTopDensity.x, u_fogBottomTopDensity.y, y) * (u_fogBottomTopDensity.y - u_fogBottomTopDensity.x) + u_fogBottomTopDensity.x;
}

float calculateFog(vec3 position)
{
	vec3 fogDelta = u_viewPosition - position;
	fogDelta.y *= u_fogYMultiplier;
	float fogDistance = length(fogDelta);
	float fogFactor = (fogIntegral(u_viewPosition.y) - fogIntegral(position.y)) / (u_viewPosition.y - position.y);
	return clamp(clamp(u_hazeStartDensity.y * (fogDistance - u_hazeStartDensity.x), 0.0, 1.0) + fogFactor * u_fogBottomTopDensity.z * fogDistance, 0.0, 1.0);
}

void main()
{
	v_texcoord = a_texcoord;
	v_color = a_color;
	v_fog = calculateFog(a_position);

	// 以相机附近的整数原点为基准的小坐标，保证片元里求导精度
	v_relativePosition = vec3(a_position.x - u_origin.x, a_position.y, a_position.z - u_origin.y);
	v_toCamera = u_viewPosition - a_position;

	gl_Position = u_viewProjectionMatrix * vec4(v_relativePosition, 1.0);

	OPENGL_POSITION_FIX;
}

#endif
