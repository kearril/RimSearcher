using System;
using System.IO;
using System.Runtime.InteropServices;
using SQLitePCL;

namespace RimSearcher.DataMod;

/// <summary>
/// 为 SQLitePCLRaw 标准 provider 提供 mod 自带的 SQLite 原生库（e_sqlite3）。
/// 原生库位于模组根目录 Native/（1.6 与 1.7 共用）：程序集目录之外的文件不会被默认探测找到，
/// 因此为 provider 程序集注册 DllImport 解析器，把 e_sqlite3 指向该文件。
/// </summary>
internal static class SqliteNativeLoader
{
    private const string LibraryName = "e_sqlite3";

    private static bool _initialized;

    /// <summary>
    /// 加载原生库并设为 SQLitePCLRaw 的 provider；必须在任何 SqliteConnection
    /// 使用之前调用。失败向上抛出：调用方记录日志，导出流程随后自然报错，不中断游戏。
    /// </summary>
    public static void Initialize(string modRootDirectory)
    {
        if (_initialized)
            return;

        var fullPath = Path.Combine(modRootDirectory, "Native", SelectPlatformLibrary());
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"SQLite native library not found: {fullPath}");

        var handle = NativeLibrary.Load(fullPath);
        NativeLibrary.SetDllImportResolver(
            typeof(SQLite3Provider_e_sqlite3).Assembly,
            (libraryName, _, _) => libraryName == LibraryName ? handle : IntPtr.Zero);
        SQLitePCL.raw.SetProvider(new SQLite3Provider_e_sqlite3());
        _initialized = true;
    }

    private static string SelectPlatformLibrary()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "e_sqlite3.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            // Apple Silicon 需 arm64 dylib（x64 在 arm64 进程中不可加载——Rosetta 不转译动态库混架构加载）。
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "libe_sqlite3.arm64.dylib"
                : "libe_sqlite3.dylib";
        return "libe_sqlite3.so";
    }
}
