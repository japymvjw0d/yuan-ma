// ShaderMod v2 公共前缀（由 ShaderSource 在 "#version 300 es" 之后自动插入）。
// Derivative 原为 GLSL 450，这里补上 GLSL ES 3.00 需要的精度声明与缺失的内置函数。

precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler3D;
precision highp sampler2DShadow;

#define fma(a, b, c) ((a) * (b) + (c))
