Derivative 光影包（HaringPro，All Rights Reserved）的纹理数据，仅供自用，不得发布：
- AtmosphereLut.rgba16f.gz：texture/Atmosphere/Final.lut（256×128×33 RGBA32F）转为半精度 RGBA16F、
  并把第 33 层（透射率/辐照度层）的 alpha 通道（原为无效数据）清零后 gzip 压缩；
- Noise2D.rgba8.gz：texture/Noise2D.png（256×256 RGBA8）的原始像素，gzip 压缩。
