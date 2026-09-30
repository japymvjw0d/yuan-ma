using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaderMod.Pipeline;

/// <summary>
/// 读取编译进 dll 的资源（GLSL 源码、Derivative 纹理数据），并展开 #include。
/// 资源名形如 "ShaderMod.Glsl/lib/Head/Common.inc"（Windows 下目录分隔符为 \，读取时统一换成 /）。
/// </summary>
internal static partial class ShaderSource
{
    const string GlslPrefix = "ShaderMod.Glsl/";

    static Dictionary<string, string> s_resourceNames;

    /// <summary>程序表：名字 → (顶点着色器宏, 片元着色器文件, 片元着色器宏)。与离线预览脚本 assemble.py 保持一致</summary>
    public static readonly Dictionary<string, (string[] VertexDefines, string Fragment, string[] FragmentDefines)> Programs = new()
    {
        ["SkyCapture"] = ([], "program/SkyCapture.fsh", []),
        ["CloudDome"] = (["NEED_ILLUMINANCE"], "program/CloudDome.fsh", []),
        ["SkyDraw"] = (["NEED_ILLUMINANCE", "FAR_PLANE"], "program/SkyDraw.fsh", []),
        ["Deferred"] = (["NEED_ILLUMINANCE", "NEED_SKY_SH"], "program/Deferred.fsh", ["CLOUDS_SHADOW"]),
        ["VolumetricLight"] = (["NEED_ILLUMINANCE"], "program/VolumetricLight.fsh", []),
        ["Composite"] = (["NEED_ILLUMINANCE"], "program/Composite.fsh", []),
        ["BloomDown"] = ([], "program/BloomDown.fsh", []),
        ["BloomBlur"] = ([], "program/BloomBlur.fsh", []),
        ["Grade"] = (["NEED_ILLUMINANCE"], "program/Grade.fsh", []),
    };

    [GeneratedRegex("^\\s*#include\\s+\"([^\"]+)\"\\s*$")]
    private static partial Regex IncludeRegex();

    static Dictionary<string, string> ResourceNames
    {
        get
        {
            if (s_resourceNames == null)
            {
                s_resourceNames = [];
                foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    s_resourceNames[name.Replace('\\', '/')] = name;
                }
            }
            return s_resourceNames;
        }
    }

    public static Stream OpenResource(string normalizedName)
    {
        if (!ResourceNames.TryGetValue(normalizedName, out string actual))
        {
            throw new FileNotFoundException($"Embedded resource not found: {normalizedName}");
        }
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(actual);
    }

    public static string ReadText(string normalizedName)
    {
        using StreamReader reader = new(OpenResource(normalizedName), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    /// <summary>读取 gzip 压缩的二进制资源</summary>
    public static byte[] ReadGzip(string normalizedName, int expectedLength)
    {
        using Stream stream = OpenResource(normalizedName);
        using GZipStream gzip = new(stream, CompressionMode.Decompress);
        byte[] data = new byte[expectedLength];
        int offset = 0;
        while (offset < expectedLength)
        {
            int read = gzip.Read(data, offset, expectedLength - offset);
            if (read <= 0)
            {
                throw new InvalidDataException($"{normalizedName}: unexpected end of data ({offset}/{expectedLength})");
            }
            offset += read;
        }
        return data;
    }

    /// <summary>组装一个着色器：#version 300 es + 宏 + Prelude + 展开 include 后的源码</summary>
    public static string Assemble(string path, IEnumerable<string> defines, IEnumerable<string> extraDefines = null)
    {
        StringBuilder sb = new();
        sb.Append("#version 300 es\n");
        foreach (string define in defines)
        {
            sb.Append("#define ").Append(define).Append('\n');
        }
        if (extraDefines != null)
        {
            foreach (string define in extraDefines)
            {
                sb.Append("#define ").Append(define).Append('\n');
            }
        }
        HashSet<string> included = [];
        Expand("Prelude.glsl", included, sb);
        Expand(path, included, sb);
        return sb.ToString();
    }

    static void Expand(string path, HashSet<string> included, StringBuilder sb)
    {
        if (!included.Add(path))
        {
            return;
        }
        string directory = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
        string text = ReadText(GlslPrefix + path);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            Match match = IncludeRegex().Match(line);
            if (match.Success)
            {
                Expand(ResolveInclude(directory, match.Groups[1].Value), included, sb);
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }
    }

    internal static string ResolveInclude(string directory, string target)
    {
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }
        List<string> parts = directory.Length > 0 ? [.. directory.Split('/')] : [];
        foreach (string part in target.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            }
            else if (part != "." && part.Length > 0)
            {
                parts.Add(part);
            }
        }
        return string.Join('/', parts);
    }

    public static (string Vertex, string Fragment) ProgramSources(string name, IEnumerable<string> extraDefines = null)
    {
        var (vertexDefines, fragment, fragmentDefines) = Programs[name];
        string[] extra = extraDefines?.ToArray() ?? [];
        return (Assemble("program/FullScreen.vsh", vertexDefines, extra), Assemble(fragment, fragmentDefines, extra));
    }
}
