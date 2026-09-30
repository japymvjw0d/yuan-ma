// ShaderMod 后处理：全屏三角形顶点着色器。
// a_position 已是裁剪空间坐标；a_texcoord 按"v=0 为画面顶部"的约定给出（与游戏渲染目标一致），
// OPENGL_POSITION_FIX 负责渲染到纹理时的 y 翻转。

// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

attribute vec3 a_position;
attribute vec2 a_texcoord;

varying vec2 v_texcoord;

void main()
{
	v_texcoord = a_texcoord;
	gl_Position = vec4(a_position.xy, 0.0, 1.0);
	OPENGL_POSITION_FIX;
}
