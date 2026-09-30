using Engine;
using Game;
using static ShaderMod.Pipeline.GlApi;

namespace ShaderMod.Pipeline;

/// <summary>
/// 相机周围每一列最高方块的高度（Terrain.GetTopHeight），按世界坐标 x、z 对 Size 取模存放（环形，相机移动无需搬移数据）。
/// 着色器用它判断某处是否露天：露天处按阳光/阴影重新分配亮度，洞穴、室内、树下保持原版亮度。
/// 每帧只更新若干行，几帧刷新一遍。
/// </summary>
internal sealed unsafe class HeightMap : IDisposable
{
    const int Size = ShaderSettings.HeightMapSize;
    const int Mask = Size - 1;

    readonly byte[] m_data = new byte[Size * Size];
    int m_nextRow;
    bool m_complete;

    public GlTexture Texture { get; } = GlTexture.Create2D(Size, Size, GL_R8, GL_RED, GL_UNSIGNED_BYTE, false, true);

    /// <summary>需在 GL 通道状态下调用（UNPACK_ALIGNMENT = 1）</summary>
    public void Update(Terrain terrain, Vector3 cameraPosition)
    {
        int rows = m_complete ? ShaderSettings.HeightMapRowsPerFrame : Size;
        int x0 = (int)MathF.Floor(cameraPosition.X) - Size / 2;
        int z0 = (int)MathF.Floor(cameraPosition.Z) - Size / 2;
        int firstRow = m_nextRow;
        for (int i = 0; i < rows; i++)
        {
            int row = (firstRow + i) & Mask;
            int z = z0 + ((row - z0) & Mask);
            int offset = row * Size;
            for (int column = 0; column < Size; column++)
            {
                int x = x0 + ((column - x0) & Mask);
                m_data[offset + column] = (byte)Math.Clamp(terrain.GetTopHeight(x, z), 0, 255);
            }
        }
        m_nextRow = (firstRow + rows) & Mask;
        m_complete = true;

        // 上传更新过的行（可能跨越末尾，分两段）
        int firstCount = Math.Min(rows, Size - firstRow);
        UploadRows(firstRow, firstCount);
        if (rows > firstCount)
        {
            UploadRows(0, rows - firstCount);
        }
    }

    void UploadRows(int row, int count)
    {
        fixed (byte* p = &m_data[row * Size])
        {
            Texture.Upload2D(0, row, Size, count, GL_RED, GL_UNSIGNED_BYTE, p);
        }
    }

    /// <summary>某点是否露天（CPU 端，用于 eyeSkylightFix）</summary>
    public static float SkyExposureAt(Terrain terrain, Vector3 position)
    {
        int top = terrain.GetTopHeight((int)MathF.Floor(position.X), (int)MathF.Floor(position.Z));
        return MathUtils.Saturate((position.Y - top) * 0.5f + 0.25f);
    }

    public void Dispose() => Texture.Dispose();
}
