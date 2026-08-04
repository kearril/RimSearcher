using System;
using System.IO;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace RimSearcher.DataMod;

/// <summary>
/// 显式加载 mod 自带的 SQLite 原生库（e_sqlite3）：Unity Mono 的 DllImport
/// 搜索路径不含 mod 目录，只能以绝对路径自行加载，再经 dynamic provider
/// 将函数指针交给 SQLitePCLRaw。
/// </summary>
internal static class NativeLibrary
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("libdl", SetLastError = true)]
    private static extern IntPtr dlopen(string fileName, int flags);

    // glibc ≥ 2.34 起 libdl 并入 libc（libdl.so 成空壳，不再导出 dlopen），由 libc 导出。
    // EntryPoint 必须显式指定：默认 EntryPoint 是方法名 dlopen_libc，而 libc 只导出 dlopen。
    [DllImport("libc", EntryPoint = "dlopen", SetLastError = true)]
    private static extern IntPtr dlopen_libc(string fileName, int flags);

    // libc 自 GLIBC_2.0 起导出 dlsym，全 glibc 版本可用；句柄与解析库无关。
    [DllImport("libc")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private const int RtldNow = 2;

    /// <summary>
    /// 按系统能力选择 dlopen：先试 libdl（glibc &lt; 2.34），失败时回退 libc（glibc ≥ 2.34）。
    /// Mono 对 DllImport 解析失败可能抛异常也可能返回零，两种都覆盖。
    /// </summary>
    private static IntPtr Dlopen(string fileName, int flags)
    {
        IntPtr handle;
        try
        {
            handle = dlopen(fileName, flags);
        }
        catch (EntryPointNotFoundException)
        {
            handle = IntPtr.Zero;
        }
        catch (DllNotFoundException)
        {
            handle = IntPtr.Zero;
        }
        return handle != IntPtr.Zero ? handle : dlopen_libc(fileName, flags);
    }

    private static IntPtr _handle = IntPtr.Zero;
    private static bool _initialized;

    /// <summary>
    /// 加载原生库并注册为 SQLitePCLRaw 的 provider；必须在任何 SqliteConnection
    /// 使用之前调用。失败向上抛出：调用方记录日志，导出流程随后自然报错，不中断游戏。
    /// </summary>
    public static void Initialize(string modRootDirectory)
    {
        if (_initialized)
            return;

        Load(modRootDirectory);
        SQLite3Provider_dynamic_cdecl.Setup("e_sqlite3", new FunctionPointerResolver(_handle));
        SQLitePCL.raw.SetProvider(new SQLite3Provider_dynamic_cdecl());
        _initialized = true;
    }

    private static void Load(string modRootDirectory)
    {
        if (_handle != IntPtr.Zero)
            return;

        var (fileName, loader) = SelectPlatformLibrary();
        var fullPath = Path.Combine(modRootDirectory, "Native", fileName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"SQLite native library not found: {fullPath}");

        _handle = loader(fullPath);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to load SQLite native library: {fullPath}");
    }

    private static (string FileName, Func<string, IntPtr> Loader) SelectPlatformLibrary()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ("e_sqlite3.dll", LoadLibrary);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Apple Silicon 需 arm64 dylib（x64 在 arm64 进程中不可加载——Rosetta 不转译动态库混架构加载）。
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? ("libe_sqlite3.arm64.dylib", path => Dlopen(path, RtldNow))
                : ("libe_sqlite3.dylib", path => Dlopen(path, RtldNow));
        }
        return ("libe_sqlite3.so", path => Dlopen(path, RtldNow));
    }

    private sealed class FunctionPointerResolver : IGetFunctionPointer
    {
        private readonly IntPtr _handle;

        public FunctionPointerResolver(IntPtr handle)
        {
            _handle = handle;
        }

        public IntPtr GetFunctionPointer(string name) =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? GetProcAddress(_handle, name)
                : dlsym(_handle, name);
    }
}
