// SPDX-License-Identifier: MIT
//
// PawnIoDevice — PawnIO 设备通信层
// 通用层，AMD/Intel 共用。从 UXTU AMDPawnIO.cs 适配。
// 参考: https://github.com/irusanov/ZenStates-Core/tree/master/PawnIo

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Douzhanzhe.HAL;

public sealed class PawnIoDevice : IDisposable
{
    private const int FunctionNameBytes = 32;
    private const uint ShareReadWrite = 0x00000003;

    // Device type + IOCTL codes (same as PawnIO driver protocol)
    private const uint DeviceType = 41394u << 16;
    private const uint IoctlLoadBinary = 0x821u << 2;
    private const uint IoctlExecuteFn = 0x841u << 2;

    private const int E_HANDLE = unchecked((int)0x80070006);

    private readonly SafeFileHandle? _device;
    private bool _disposed;

    private PawnIoDevice(SafeFileHandle? deviceHandle)
    {
        _device = deviceHandle;
    }

    /// <summary>设备是否已打开且可用</summary>
    public bool IsLoaded => _device is { IsInvalid: false, IsClosed: false };

    /// <summary>释放设备句柄</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device?.Close();
        _device?.Dispose();
    }

    // ----------------------------------------------------------------
    // 静态工厂: 从文件加载模块
    // ----------------------------------------------------------------

    /// <summary>从 .bin 文件加载 PawnIO 模块并返回设备实例</summary>
    public static PawnIoDevice LoadModuleFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("PawnIO 模块文件未找到", filePath);

        byte[] moduleBytes = File.ReadAllBytes(filePath);
        return LoadModule(moduleBytes);
    }

    private static PawnIoDevice LoadModule(byte[] moduleBytes)
    {
        // 尝试新版设备路径
        var raw = CreateFile(
            @"\\?\GLOBALROOT\Device\PawnIO",
            0x80000000 | 0x40000000, // GENERIC_READ | GENERIC_WRITE
            ShareReadWrite,
            IntPtr.Zero,
            3u,
            0u,
            IntPtr.Zero);

        // 回退旧版设备路径
        if (raw == IntPtr.Zero || raw.ToInt64() == -1)
        {
            raw = CreateFile(
                @"\\.\PawnIO",
                0x80000000 | 0x40000000,
                ShareReadWrite,
                IntPtr.Zero,
                3u,
                0u,
                IntPtr.Zero);

            if (raw == IntPtr.Zero || raw.ToInt64() == -1)
                return new PawnIoDevice(null);

            AppLog.Write("PawnIO", "使用旧版设备路径 \\.\\PawnIO");
        }

        try
        {
            var success = DeviceIoControl(
                raw,
                (DeviceType | IoctlLoadBinary),
                moduleBytes,
                (uint)moduleBytes.Length,
                null,
                0u,
                out _,
                IntPtr.Zero);

            if (!success)
            {
                CloseHandle(raw);
                return new PawnIoDevice(null);
            }

            AppLog.Write("PawnIO", $"模块加载成功 ({moduleBytes.Length} bytes)");
            return new PawnIoDevice(new SafeFileHandle(raw, ownsHandle: true));
        }
        catch
        {
            try { CloseHandle(raw); } catch { }
            return new PawnIoDevice(null);
        }
    }

    // ----------------------------------------------------------------
    // 执行函数
    // ----------------------------------------------------------------

    /// <summary>执行模块函数，返回输出 long[]</summary>
    public long[] Execute(string name, long[] inputs, int outLength)
    {
        ArgumentNullException.ThrowIfNull(name);
        inputs ??= [];

        var outputs = new long[outLength];
        var hr = ExecuteHr(name, inputs, (uint)inputs.Length, outputs, (uint)outLength, out var returned);

        if (hr != 0 || returned == 0)
            return outputs;

        if (returned < (uint)outLength)
        {
            var trimmed = new long[returned];
            Array.Copy(outputs, trimmed, (int)returned);
            return trimmed;
        }

        return outputs;
    }

    /// <summary>执行函数，返回 HRESULT (0 = S_OK)</summary>
    public int ExecuteHr(string name, long[] inBuffer, uint inSize, long[] outBuffer, uint outSize, out uint returnSize)
    {
        ArgumentNullException.ThrowIfNull(name);
        inBuffer ??= [];
        outBuffer ??= [];

        if (inBuffer.Length < inSize)
            throw new ArgumentOutOfRangeException(nameof(inSize));
        if (outBuffer.Length < outSize)
            throw new ArgumentOutOfRangeException(nameof(outSize));

        if (!IsLoaded)
        {
            returnSize = 0;
            return E_HANDLE;
        }

        var request = BuildRequest(name, inBuffer, inSize);
        var response = new byte[outSize * 8];

        var ok = DeviceIoControl(
            _device!,
            (DeviceType | IoctlExecuteFn),
            request,
            (uint)request.Length,
            response,
            (uint)response.Length,
            out var bytesReturned,
            IntPtr.Zero);

        if (!ok)
        {
            returnSize = 0;
            return Marshal.GetHRForLastWin32Error();
        }

        var copyBytes = Math.Min((int)bytesReturned, outBuffer.Length * 8);
        Buffer.BlockCopy(response, 0, outBuffer, 0, copyBytes);

        returnSize = bytesReturned / 8;
        return 0;
    }

    private static byte[] BuildRequest(string functionName, long[] args, uint argCount)
    {
        // 协议布局: 32 字节函数名 + N × 8 字节参数
        var buffer = new byte[FunctionNameBytes + (argCount * 8)];

        var nameBytes = Encoding.ASCII.GetBytes(functionName);
        var nameCopy = Math.Min(FunctionNameBytes - 1, nameBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, 0, nameCopy);

        if (argCount > 0)
            Buffer.BlockCopy(args, 0, buffer, FunctionNameBytes, (int)argCount * 8);

        return buffer;
    }

    // ----------------------------------------------------------------
    // Native interop
    // ----------------------------------------------------------------

    [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        byte[]? lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        [Out] byte[]? lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
