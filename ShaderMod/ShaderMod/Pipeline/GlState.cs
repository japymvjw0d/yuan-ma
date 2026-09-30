using static ShaderMod.Pipeline.GlApi;

namespace ShaderMod.Pipeline;

/// <summary>
/// 保存 / 恢复本模组会改动的 GL 状态。
/// 游戏引擎（GLWrapper）缓存了当前绑定的程序、帧缓冲、纹理和各种开关，并据此跳过重复调用；
/// 只要本模组用完后把真实 GL 状态恢复原样，引擎的缓存就仍然正确，不会画错。
/// </summary>
internal unsafe struct GlState
{
    public const int SavedTextureUnits = 16;

    int m_program, m_drawFramebuffer, m_readFramebuffer, m_activeTexture, m_vertexArray;
    int m_unpackAlignment, m_unpackRowLength, m_unpackBuffer, m_depthFunc;
    fixed int m_viewport[4];
    fixed int m_scissor[4];
    fixed int m_texture2D[SavedTextureUnits];
    fixed int m_texture3D[SavedTextureUnits];
    fixed int m_sampler[SavedTextureUnits];
    fixed byte m_colorMask[4];
    byte m_depthMask, m_depthTest, m_blend, m_cull, m_scissorTest, m_stencilTest, m_polygonOffset;

    public static GlState Save()
    {
        GlState s = default;
        s.m_program = GetInteger(GL_CURRENT_PROGRAM);
        s.m_drawFramebuffer = GetInteger(GL_DRAW_FRAMEBUFFER_BINDING);
        s.m_readFramebuffer = GetInteger(GL_READ_FRAMEBUFFER_BINDING);
        s.m_activeTexture = GetInteger(GL_ACTIVE_TEXTURE);
        s.m_vertexArray = GetInteger(GL_VERTEX_ARRAY_BINDING);
        s.m_unpackAlignment = GetInteger(GL_UNPACK_ALIGNMENT);
        s.m_unpackRowLength = GetInteger(GL_UNPACK_ROW_LENGTH);
        s.m_unpackBuffer = GetInteger(GL_PIXEL_UNPACK_BUFFER_BINDING);
        s.m_depthFunc = GetInteger(GL_DEPTH_FUNC);
        glGetIntegerv(GL_VIEWPORT, s.m_viewport);
        glGetIntegerv(GL_SCISSOR_BOX, s.m_scissor);
        for (int i = 0; i < SavedTextureUnits; i++)
        {
            glActiveTexture(GL_TEXTURE0 + (uint)i);
            s.m_texture2D[i] = GetInteger(GL_TEXTURE_BINDING_2D);
            s.m_texture3D[i] = GetInteger(GL_TEXTURE_BINDING_3D);
            s.m_sampler[i] = GetInteger(GL_SAMPLER_BINDING);
        }
        glActiveTexture((uint)s.m_activeTexture);
        glGetBooleanv(GL_COLOR_WRITEMASK, s.m_colorMask);
        byte depthMask;
        glGetBooleanv(GL_DEPTH_WRITEMASK, &depthMask);
        s.m_depthMask = depthMask;
        s.m_depthTest = glIsEnabled(GL_DEPTH_TEST);
        s.m_blend = glIsEnabled(GL_BLEND);
        s.m_cull = glIsEnabled(GL_CULL_FACE);
        s.m_scissorTest = glIsEnabled(GL_SCISSOR_TEST);
        s.m_stencilTest = glIsEnabled(GL_STENCIL_TEST);
        s.m_polygonOffset = glIsEnabled(GL_POLYGON_OFFSET_FILL);
        return s;
    }

    /// <summary>本模组的全屏通道统一使用的状态：不混合、不测深度、不剔除、不裁剪、写全部颜色</summary>
    public static void SetPassState()
    {
        glDisable(GL_BLEND);
        glDisable(GL_DEPTH_TEST);
        glDisable(GL_CULL_FACE);
        glDisable(GL_SCISSOR_TEST);
        glDisable(GL_STENCIL_TEST);
        glDisable(GL_POLYGON_OFFSET_FILL);
        glColorMask(1, 1, 1, 1);
        glDepthMask(0);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    }

    public readonly void Restore()
    {
        glUseProgram((uint)m_program);
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)m_drawFramebuffer);
        glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)m_readFramebuffer);
        glBindVertexArray((uint)m_vertexArray);
        glPixelStorei(GL_UNPACK_ALIGNMENT, m_unpackAlignment);
        glPixelStorei(GL_UNPACK_ROW_LENGTH, m_unpackRowLength);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER, (uint)m_unpackBuffer);
        glDepthFunc((uint)m_depthFunc);
        glViewport(m_viewport[0], m_viewport[1], m_viewport[2], m_viewport[3]);
        glScissor(m_scissor[0], m_scissor[1], m_scissor[2], m_scissor[3]);
        for (int i = 0; i < SavedTextureUnits; i++)
        {
            glActiveTexture(GL_TEXTURE0 + (uint)i);
            glBindTexture(GL_TEXTURE_2D, (uint)m_texture2D[i]);
            glBindTexture(GL_TEXTURE_3D, (uint)m_texture3D[i]);
            glBindSampler((uint)i, (uint)m_sampler[i]);
        }
        glActiveTexture((uint)m_activeTexture);
        glColorMask(m_colorMask[0], m_colorMask[1], m_colorMask[2], m_colorMask[3]);
        glDepthMask(m_depthMask);
        SetEnabled(GL_DEPTH_TEST, m_depthTest);
        SetEnabled(GL_BLEND, m_blend);
        SetEnabled(GL_CULL_FACE, m_cull);
        SetEnabled(GL_SCISSOR_TEST, m_scissorTest);
        SetEnabled(GL_STENCIL_TEST, m_stencilTest);
        SetEnabled(GL_POLYGON_OFFSET_FILL, m_polygonOffset);
    }

    static void SetEnabled(uint cap, byte enabled)
    {
        if (enabled != 0)
        {
            glEnable(cap);
        }
        else
        {
            glDisable(cap);
        }
    }
}
