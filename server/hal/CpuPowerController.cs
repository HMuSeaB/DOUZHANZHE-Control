// SPDX-License-Identifier: MIT
// CpuPowerController -- Windows 鐢垫簮璁″垝 CPU 鎺у埗灏佽
// 鍩轰簬铔熼緳鎺у埗鍙伴€嗗悜鍒嗘瀽 (reference-consoles.md 搂2)
// 搴曞眰: powercfg.exe (鏃犻渶绠＄悊鍛樻潈闄愶紝鏃犻渶椹卞姩)

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Douzhanzhe.HAL;

/// <summary>
/// CPU 鎬ц兘鎺у埗 鈥?閫氳繃 Windows powercfg 鐢垫簮璁″垝 API
/// 鏀寔: 棰戠巼闄愬埗 / 鍏抽棴鐫块 / 鏍稿績鏁扮櫨鍒嗘瘮 / 鍔熻€楃瓥鐣?
/// </summary>
public sealed class CpuPowerController : IDisposable
{
    // 鈹€鈹€ 甯搁噺 GUID 鈹€鈹€
    // 鐢垫簮鏂规瀛愮粍: 澶勭悊鍣ㄧ數婧愯缃?(鏍囧噯 Windows GUID)
    private const string SUB_PROCESSOR = "54533251-82be-4824-96c1-47b60b740d00";

    // 澶勭悊鍣ㄩ鐜囬檺鍒?(OEM 鎵╁睍锛岃洘榫欎娇鐢ㄦ GUID)
    private const string SET_PROC_FREQ_LIMIT = "75b0ae3f-bce0-45a7-8c89-c9611c25e100";

    // Processor performance boost mode (鏍囧噯 Windows)
    private const string SET_PERF_BOOST = "be337238-0d82-4146-a960-4f3749d470c7";

    // Processor maximum state % (鏍囧噯 Windows)
    private const string SET_PROC_MAX_STATE = "0cc5b647-c1df-4637-891a-dec35c318583";

    // Processor minimum state % (鏍囧噯 Windows)
    private const string SET_PROC_MIN_STATE = "893dee8e-2bef-41e0-89c6-b55d0929964c";

    // Processor power throttling max (鏍囧噯 Windows)
    private const string SET_PROC_THROTTLE_MAX = "8baa4a8a-14c6-4451-8e8b-14bdbd197537";

    // Processor hardware threading (鏍囧噯 Windows)
    private const string SET_PROC_HW_THREADING = "ea062031-0e34-4ff1-9b6d-eb1059334028";

    // Processor idle demotion (鏍囧噯 Windows)
    private const string SET_PROC_IDLE_DEMOTION = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";

    /// <summary>
    /// Ryzen 9 8940HX 鍩虹棰戠巼 (WMI MaxClockSpeed 鈮?2401 MHz)
    /// </summary>
    private const int CPU_BASE_CLOCK_MHZ = 2400;

    private const int TimeoutMs = 3000;
    private bool _disposed;

    // 鈹€鈹€ 鍏叡 API 鈹€鈹€

    /// <summary>
    /// 璁剧疆 CPU 鏈€澶ч鐜囬檺鍒?(MHz)
    /// 璁句负 0 琛ㄧず鍙栨秷闄愬埗
    /// </summary>
    public async Task SetFreqLimitAsync(int mhz)
    {
        if (mhz < 0) throw new ArgumentOutOfRangeException(nameof(mhz));
        var scheme = GetActiveScheme();
        await DisableOverlayAsync();
        // 鐩存帴鍐欏叆 AC + DC
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_FREQ_LIMIT, mhz.ToString());
        await Task.Delay(50);
        // 閲嶆柊婵€娲绘柟妗堜娇璁剧疆鐢熸晥
        await SetActiveSchemeAsync(scheme);
    }

    /// <summary>
    /// 鍚敤/绂佺敤閿侀妯″紡 (鍘熷悕"鍏抽棴鐫块")
    /// 绂佺敤(閿侀): min=max=100% + boost=2锛孋PU 閽夋鍦ㄥ綋鍓嶉鐜囦笂闄?
    ///   - 涓嶄慨鏀?freq_limit锛岀敱鐢ㄦ埛閫氳繃棰戠巼闄愬埗婊戝潡鎺у埗涓婇檺
    ///   - 鑻ユ湭璁鹃鐜囬檺鍒讹紝鍒?CPU 璺戝湪鍏ㄦ牳鏈€澶х澘棰?
    /// 鍚敤(姝ｅ父): min=5% + max=100% + boost=2锛屾仮澶嶆甯告寜闇€璋冮
    ///   - 5% 涓?Windows 骞宠　鐢垫簮璁″垝鍑哄巶榛樿鏈€灏忓€硷紝閬垮厤浣庤礋杞借繃搴﹂檷棰?(0.5 GHz)
    /// </summary>
    public async Task SetTurboAsync(bool enabled)
    {
        var scheme = GetActiveScheme();
        await DisableOverlayAsync();
        if (enabled)
        {
            // 鎭㈠姝ｅ父: 鍏佽棰戠巼鏍规嵁璐熻浇鍔ㄦ€佽皟鑺?
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MIN_STATE, "5");
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MAX_STATE, "100");
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PERF_BOOST, "2");
        }
        else
        {
            // 閿侀妯″紡: min=max=100% 寮哄埗 CPU 濮嬬粓杩愯鍦ㄦ渶楂樺彲鐢ㄩ鐜?
            // 涓嶅姩 freq_limit 鈥?鐢ㄦ埛鍙€氳繃棰戠巼闄愬埗婊戝潡璁惧畾鎯宠鐨勯攣瀹氶鐜?
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MIN_STATE, "100");
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MAX_STATE, "100");
            await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PERF_BOOST, "2");
        }
        await Task.Delay(50);
        await SetActiveSchemeAsync(scheme);
    }

    /// <summary>
    /// 璁剧疆 CPU 鏍稿績鏁伴檺鍒?(0-100%)
    /// 璁句负 100 琛ㄧず鏃犻檺鍒?
    /// </summary>
    public async Task SetCoreLimitAsync(int percent)
    {
        if (percent < 0 || percent > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "蹇呴』 0-100");
        var scheme = GetActiveScheme();
        await DisableOverlayAsync();
        var val = percent.ToString();
        // 铔熼緳鍚屾: 3 涓弬鏁板悓鏃惰缃?
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_THROTTLE_MAX, val);
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MAX_STATE, val);
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_HW_THREADING, val);
        await Task.Delay(50);
        await SetActiveSchemeAsync(scheme);
    }

    /// <summary>
    /// 鎭㈠鎵€鏈?CPU 闄愬埗鍒伴粯璁?(鏃犻檺鍒?
    /// </summary>
    public async Task ResetAllAsync()
    {
        var scheme = GetActiveScheme();
        await DisableOverlayAsync();
        // 棰戠巼闄愬埗褰掗浂 (鍙栨秷)
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_FREQ_LIMIT, "0");
        // 鐫块鍚敤 (婵€杩涙ā寮?
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MIN_STATE, "0");
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PERF_BOOST, "2");
        // 鏍稿績鏁?100%
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_THROTTLE_MAX, "100");
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_MAX_STATE, "100");
        await SetPowerValueAsync(scheme, SUB_PROCESSOR, SET_PROC_HW_THREADING, "100");
        await Task.Delay(50);
        await SetActiveSchemeAsync(scheme);
    }

    /// <summary>
    /// 璇诲彇褰撳墠 CPU 鐢垫簮璁剧疆鐘舵€?
    /// </summary>
    public CpuPowerStatus GetStatus()
    {
        var status = new CpuPowerStatus();
        try
        {
            // 閿侀鐘舵€? 閫氳繃 min_state 鍒ゆ柇 (min=100% 琛ㄧず閿侀/鍏抽棴鐫块)
            var minStr = QueryPowerValue(SUB_PROCESSOR, SET_PROC_MIN_STATE);
            if (int.TryParse(minStr, out var minState))
                status.TurboEnabled = minState < 100; // min<100% 琛ㄧず鍏佽璋冮锛屽嵆鐫块姝ｅ父
            else
                status.TurboEnabled = true;

            // 璇诲彇鏍稿績鏁伴檺鍒?
            var coreStr = QueryPowerValue(SUB_PROCESSOR, SET_PROC_MAX_STATE);
            if (int.TryParse(coreStr, out var core))
                status.CoreLimitPercent = core;

            // 璇诲彇棰戠巼闄愬埗
            var freqStr = QueryPowerValue(SUB_PROCESSOR, SET_PROC_FREQ_LIMIT);
            if (int.TryParse(freqStr, out var freq))
                status.FreqLimitMhz = freq;

            status.Available = true;
        }
        catch (Exception ex)
        {
            AppLog.Write("CpuPower", $"GetStatus error: {ex.Message}");
            status.Available = false;
        }
        return status;
    }

    // 鈹€鈹€ 鍐呴儴瀹炵幇 鈹€鈹€

    private string GetActiveScheme()
    {
        var output = RunPowerCfg("/getactivescheme");
        // 杈撳嚭鏍煎紡: "鐢垫簮鏂规 GUID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx  (鏂规鍚嶇О)"
        var match = Regex.Match(output, @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException("鏃犳硶鑾峰彇褰撳墠鐢垫簮鏂规 GUID");
        return match.Groups[1].Value;
    }

    private async Task DisableOverlayAsync()
    {
        await RunPowerCfgAsync("/setactive SCHEME_CURRENT");
        await RunPowerCfgAsync("/overlaysetactive overlay_scheme_none");
        await Task.Delay(50);
    }

    private async Task SetActiveSchemeAsync(string scheme)
    {
        await RunPowerCfgAsync($"/setactive {scheme}");
    }

    private async Task SetPowerValueAsync(string scheme, string subGroup, string setting, string value)
    {
        await RunPowerCfgAsync($"/setacvalueindex {scheme} {subGroup} {setting} {value}");
        await RunPowerCfgAsync($"/setdcvalueindex {scheme} {subGroup} {setting} {value}");
    }

    private string QueryPowerValue(string subGroup, string setting)
    {
        var output = RunPowerCfg("/query SCHEME_CURRENT " + subGroup + " " + setting);
        // powercfg /query 杈撳嚭涓紝鏈€鍚庝袱琛屽缁堟槸:
        //   "褰撳墠浜ゆ祦鐢垫簮璁剧疆绱㈠紩: 0xHHHH" (AC)
        //   "褰撳墠鐩存祦鐢垫簮璁剧疆绱㈠紩: 0xHHHH" (DC)
        // 涓枃鏍囩鍦?UTF-8/GBK 缂栫爜涓嶅尮閰嶆椂浼氫贡鐮侊紝浣?0x 鍗佸叚杩涘埗鏁板€兼槸绾?ASCII锛屼笉鍙楀奖鍝嶃€?
        // 鍥犳鐢ㄥ叏灞€鍖归厤鍙栧€掓暟绗簩涓?0x 鍊间綔涓?AC 璁剧疆銆?
        var hexMatches = Regex.Matches(output, @"0x([0-9a-fA-F]+)");
        if (hexMatches.Count >= 2)
        {
            var acHex = hexMatches[^2].Groups[1].Value;
            if (int.TryParse(acHex, System.Globalization.NumberStyles.HexNumber, null, out var val))
                return val.ToString();
        }
        else if (hexMatches.Count == 1)
        {
            if (int.TryParse(hexMatches[0].Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var val))
                return val.ToString();
        }
        // 鍥為€€: 灏濊瘯鍗佽繘鍒舵牸寮?(浠?ASCII 鏁板瓧)
        var match = Regex.Match(output, @"(\d+)\s*\r?\n[^\r\n]*(\d+)\s*$");
        if (match.Success) return match.Groups[1].Value;
        return "0";
    }

    private string RunPowerCfg(string args)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };
        p.Start();
        if (!p.WaitForExit(TimeoutMs))
        {
            p.Kill();
            throw new TimeoutException("powercfg timed out: " + args);
        }
        return p.StandardOutput.ReadToEnd().Trim();
    }

    private async Task RunPowerCfgAsync(string args)
    {
        await Task.Run(() => RunPowerCfg(args));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

public struct CpuPowerStatus
{
    public bool Available;
    public bool TurboEnabled;
    public int CoreLimitPercent;  // 0-100
    public int FreqLimitMhz;     // 0 = 鏃犻檺鍒?
}
