import subprocess, sys, os, xml.etree.ElementTree as ET, re, time, html
from pathlib import Path

TEST_PROJ  = Path("BackgroundControl.Tests/BackgroundControl.Tests.csproj")
RESULTS_DIR = Path("BackgroundControl.Tests/TestResults")
TRX_FILE   = RESULTS_DIR / "test-results.trx"
HTML_FILE  = RESULTS_DIR / "test-report.html"

NAME_CN = {
    "Constructor_WithPanel_CreatesInstance": "构造函数传入面板创建实例",
    "Constructor_NullPanel_ThrowsArgumentNullException": "构造函数传入空面板抛出异常",
    "StartCmd_ProcessExists": "启动命令后进程存在",
    "StopCmd_ProcessExits": "停止命令后进程退出",
    "StartCmd_UseShellExecuteFalse_RedirectsDisabled": "禁用 ShellExecute 时重定向关闭",
    "ServerStartsAndStops": "服务器启动和停止",
    "GetRootFullPath_ReturnsNormalized": "获取根路径返回规范化路径",
    "IsInsideRoot_SamePath_ReturnsTrue": "相同路径在根目录内返回真",
    "IsInsideRoot_SubPath_ReturnsTrue": "子路径在根目录内返回真",
    "LoginAnonymous_Returns230": "匿名登录返回 230",
    "Stor_UploadFile_WritesToDisk": "上传文件写入磁盘",
    "Retr_DownloadFile_ReturnsContent": "下载文件返回内容",
    "Dele_File_RemovesFromDisk": "删除文件从磁盘移除",
    "Mkd_CreatesDirectory": "创建目录",
    "Rmd_RemovesDirectory": "删除目录",
    "List_ReturnsDirectoryListing": "列出目录内容",
    "TypeI_SetsBinaryMode": "设置二进制模式",
    "TwoClients_Simultaneous_BothConnect": "两个客户端同时连接",
    "Get_Root_ReturnsDirectoryListing": "获取根路径返回目录列表",
    "Get_ExistingFile_ReturnsContent": "获取现有文件返回内容",
    "Get_NonexistentFile_Returns404": "获取不存在文件返回 404",
    "Post_UploadFile_WritesToDisk": "上传文件写入磁盘",
    "Get_WithRange_ReturnsPartialContent": "带 Range 头返回部分内容",
    "Delete_ExistingFile_Returns200AndRemovesFile": "删除现有文件返回 200 并移除",
    "Delete_NonexistentFile_Returns404": "删除不存在文件返回 404",
    "Get_PathTraversal_Returns403": "路径穿越请求返回 403",
    "Post_LargeFile_UploadsCorrectly": "上传大文件正确",
    "Get_ConcurrentDownloads_AllSucceed": "并发下载全部成功",
    "Parse1000Intervals_Under100ms": "解析 1000 个间隔在 100ms 内",
    "Parse5000Intervals_Under200ms": "解析 5000 个间隔在 200ms 内",
    "ParseBitsPerSec_VariousUnits_AllCorrect": "解析各种单位比特率全部正确",
    "TryParseTextInterval_ParsesAllLines_Rapid": "批量解析文本间隔快速",
    "IntervalRegex_MatchesAllSampleLines": "正则匹配所有示例行",
    "StartClient_Tcp_ReceivesIntervalData": "TCP 客户端接收间隔数据",
    "StartClient_Udp_ReportsJitterAndLoss": "UDP 客户端报告抖动和丢包",
    "StartClient_Parallel5_AllStreamsComplete": "5 个并行流全部完成",
    "StartClient_Bandwidth10M_ResultCloseToLimit": "带宽 10M 结果接近限制",
    "StartStop_RapidCycles_NoCrash": "快速启停不崩溃",
    "ServerMode_ClientConnects_ReceivesData": "服务器模式客户端连接接收数据",
    "OpenPort_Succeeds": "打开串口成功",
    "OpenPort_WithDifferentBaudRates_Succeeds": "不同波特率打开成功",
    "OpenPort_ReadWriteTimeout_DoesNotThrow": "读写超时不抛出异常",
    "ClosePort_IsOpenFalse": "关闭串口后 IsOpen 为假",
    "Open_Close_Reopen_Succeeds": "打开关闭再打开成功",
    "ConnectToDevice_IsConnected": "连接设备后 IsConnected 为真",
    "ConnectToDevice_ShellStream_CanRead": "连接设备后 ShellStream 可读",
    "LinuxCommand_Ls_ExecutesViaSsh": "Linux ls 命令通过 SSH 执行",
    "SwitchToTelnet_SendsCommandAndGetsResponse": "切换 Telnet 发送命令并获取响应",
    "TelnetConnectAndSend_ReceivesResponse": "Telnet 连接发送并接收响应",
    "StripAnsi_RemovesEscapeSequence": "移除转义序列",
    "StripAnsi_RemovesOSCSequence": "移除 OSC 序列",
    "StripAnsi_RemovesControlChars": "移除控制字符",
    "StripAnsi_NormalizesNewlines": "规范化换行符",
    "StripAnsi_EmptyInput_ReturnsEmpty": "空输入返回空",
    "StripAnsi_NullInput_ReturnsEmpty": "Null 输入返回空",
    "StripAnsi_NoAnsi_ReturnsOriginal": "无 ANSI 返回原文",
    "IsLinuxCommand_ReturnsExpected": "IsLinuxCommand 返回预期值",
    "IsDirectCommand_ReturnsExpected": "IsDirectCommand 返回预期值",
    "ClassifyCommand_LinuxCommand_ReturnsIsLinuxTrue": "Linux 命令分类返回 IsLinux 为真",
    "ClassifyCommand_DirectCommand_ReturnsIsLinuxFalse": "直接命令分类返回 IsLinux 为假",
    "ClassifyCommand_RuleMatch_ReturnsMappedCommand": "规则匹配返回映射命令",
    "ClassifyCommand_UnknownCommand_ReturnsIsLinuxFalseForGetPrefix": "未知命令为 Get 前缀返回 IsLinux 假",
    "ClassifyCommand_UnknownCommand_ReturnsIsLinuxTrueForNonDirect": "未知命令非直接返回 IsLinux 真",
    "StripAnsi_MixedContent_ProducesCleanText": "混合内容生成干净文本",
    "StripAnsi_MultipleLines_PreservesNewlines": "多行保留换行",
    "StripAnsi_ControlChars_Removed": "控制字符已移除",
    "StripAnsi_RealWorldTerminalOutput": "真实终端输出处理",
    "CompileRules_ValidPattern_ReturnsCompiledRegex": "有效模式返回编译正则",
    "CompileRules_InvalidPattern_Skipped": "无效模式跳过",
    "CompileRules_MultipleValid_AllCompiled": "多个有效全部编译",
    "LoadRules_FileNotExists_ReturnsEmpty": "规则文件不存在返回空",
    "LoadRules_ValidJson_Deserializes": "有效 JSON 反序列化",
    "LoadRules_CorruptedJson_ReturnsEmpty": "损坏 JSON 返回空",
    "LoadRules_CaseInsensitiveProperty": "属性名不区分大小写",
    "HighlightPattern_Match_WrapsWithColorCode": "匹配用颜色码包裹",
    "HighlightPattern_NoMatch_OriginalString": "无匹配返回原字符串",
    "HighlightPattern_MultiplePatterns_AllColored": "多模式全部着色",
    "HighlightPattern_EmptyInput_EmptyOutput": "空输入空输出",
    "HighlightPattern_HtmlEntityPreserved": "HTML 实体保留",
    "ParseInterval_ServerMode_ParsesReceiverBitsPerSec": "服务器模式解析接收端比特率",
    "ParseInterval_ServerReceiver_ParsesReceiverBitsPerSec": "服务器接收端解析接收端比特率",
    "ParseInterval_UdpJitter_ParsesJitterMs": "UDP 抖动解析 JitterMs",
    "ParseInterval_UdpLoss_ParsesLoss": "UDP 丢包解析 Loss",
    "ParseInterval_NonIntervalLine_Ignored": "非间隔行忽略",
    "ParseFinalResult_ExtractsSenderAndReceiver": "解析最终结果提取发送端和接收端",
    "ParseFinalResult_OnlySender_ReturnsPartial": "仅发送端返回部分结果",
    "ParseFinalResult_NoMatch_NoEvent": "无匹配不触发事件",
    "IsAvailable_ReturnsTrueForMainAssembly": "IsAvailable 对主程序集返回真",
    "ParseBitsPerSec_ConvertsCorrectly": "解析比特率转换正确",
    "ResolveSafePath_WithinRoot_ReturnsFullPath": "根目录内返回完整路径",
    "ResolveSafePath_RootItself_ReturnsRoot": "根目录本身返回根",
    "ResolveSafePath_PathTraversal_Throws": "路径穿越抛出异常",
    "GetRootFullPath_AppendsSeparator": "根路径追加分隔符",
    "MatchCommand_ExactMatch_ReturnsRuleCommand": "精确匹配返回规则命令",
    "MatchCommand_ContainsMatch_ReturnsRuleCommand": "包含匹配返回规则命令",
    "MatchCommand_NoMatch_ReturnsUnknown": "无匹配返回 Unknown",
    "MatchCommand_BestScoreWins_OverTotalScore": "最佳分数优先于总分",
    "MatchCommand_TieBreakByTotalScore": "平局按总分决胜",
    "MatchCommand_DirectCommand_ReturnsDirect": "直接命令返回 Direct",
    "SaveAndLoad_RoundTrip": "保存和加载往返",
    "Load_FileNotExists_ReturnsDefaultRules": "文件不存在返回默认规则",
    "Load_CorruptedJson_ReturnsDefaultRules": "损坏 JSON 返回默认规则",
    "DefaultRules_HaveAtLeast30Entries": "默认规则至少 30 条",
    "DefaultRules_EachHasNonEmptyCommand": "每条规则有非空命令",
    "EncryptDecrypt_RoundTrip": "加密解密往返",
    "Encrypt_ProducesDifferentOutput_ForSamePlaintext": "相同明文加密输出不同",
    "Load_FileNotExists_ReturnsEmpty": "文件不存在返回空",
    "SaveAndLoad_RoundTrip_PreservesEntries": "保存加载往返保留条目",
    "Load_CorruptedJson_ReturnsEmpty": "损坏 JSON 返回空",
    "WriteInput_EnterWithBuffer_CallsHandler": "输入回车带缓冲区调用处理",
    "WriteInput_Backspace_RemovesLastChar": "退格删除最后字符",
    "WriteInput_Backspace_EmptyBufferNoOp": "退格空缓冲区无操作",
    "WriteInput_PrintableChars_AppendToBuffer": "可打印字符追加到缓冲区",
    "WriteInput_Newline_Skipped": "换行符跳过",
    "WriteInput_ControlChar_PassedToStream": "控制字符传递到流",
    "HasPendingInput_InitiallyFalse": "初始无待处理输入",
    "WriteInput_EnterWithEmptyBuffer_DoesNotCallHandler": "空缓冲区回车不调用处理",
    "Load_FileNotExists_ReturnsDefaults": "文件不存在返回默认值",
    "SaveAndLoad_RoundTrip_PreservesValues": "保存加载往返保留值",
    "Load_CorruptedJson_ReturnsDefaults": "损坏 JSON 返回默认值",
    "Info_WritesFormattedEntry": "Info 写入格式化条目",
    "Warn_WritesFormattedEntry": "Warn 写入格式化条目",
    "Error_WritesFormattedEntry": "Error 写入格式化条目",
    "Debug_WritesFormattedEntry": "Debug 写入格式化条目",
    "LogAppended_EventFiresOnEachWrite": "每次写入触发事件",
    "Snapshot_ReturnsAllEntries": "快照返回所有条目",
    "CircularBuffer_TrimsAt500": "环形缓冲区在 500 处裁剪",
    "LogLine_HasTimestampAndSource": "日志行包含时间戳和来源",
}

def run_tests():
    RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    print("=== Running tests ===")
    r = subprocess.run(
        ["dotnet", "test", str(TEST_PROJ),
         "--logger", "console;verbosity=detailed",
         "--logger", f"trx;LogFileName={TRX_FILE.name}"],
        cwd=Path.cwd())
    return (r.returncode == 0 or r.returncode == 1)

ns = {"trx": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def build_report():
    print("=== Generating HTML report ===")
    tree = ET.parse(TRX_FILE)
    root = tree.getroot()

    counters = root.find(".//trx:ResultSummary/trx:Counters", ns)
    total  = int(counters.get("total"))
    passed = int(counters.get("passed"))
    failed = int(counters.get("failed"))
    skipped = int(counters.get("aborted", 0)) + int(counters.get("inconclusive", 0)) + int(counters.get("error", 0))

    defs = {}
    for d in root.findall(".//trx:UnitTest", ns):
        defs[d.get("id")] = d

    results = []
    for r in root.findall(".//trx:UnitTestResult", ns):
        test_id = r.get("testId")
        d = defs.get(test_id)
        if d is None:
            continue
        tm = d.find("trx:TestMethod", ns)
        class_name = tm.get("className")
        method_name = tm.get("name")
        full_name = f"{class_name}.{method_name}"

        test_name = r.get("testName")
        params = ""
        if len(test_name) > len(full_name):
            params = test_name[len(full_name):]

        cls = class_name
        i = cls.rfind(".")
        if i >= 0:
            cls = cls[i+1:]
        cls = re.sub(r"Tests$", "", cls)

        outcome = r.get("outcome")
        dur_raw = r.get("duration")
        duration = 0
        if dur_raw:
            parts = dur_raw.split(":")
            if len(parts) == 3:
                duration = int(parts[0])*3600000 + int(parts[1])*60000 + int(float(parts[2])*1000)

        error_msg = ""
        output = r.find("trx:Output", ns)
        if output is not None:
            ei = output.find("trx:ErrorInfo", ns)
            if ei is not None:
                msg_el = ei.find("trx:Message", ns)
                if msg_el is not None and msg_el.text:
                    error_msg = msg_el.text

        eng = method_name
        cn = NAME_CN.get(eng, eng)
        results.append({
            "class": cls,
            "name": eng,
            "name_cn": cn,
            "params": params,
            "outcome": outcome,
            "duration": duration,
            "error": error_msg,
        })

    groups = {}
    for r in results:
        groups.setdefault(r["class"], []).append(r)

    ts = time.strftime("%Y-%m-%d %H:%M:%S")

    CSS = ("body{font-family:'Segoe UI',sans-serif;margin:30px 40px;background:#f5f5f5}"
           "h1{color:#333;border-bottom:2px solid #007bff;padding-bottom:10px}"
           ".summary{background:#fff;border-radius:8px;padding:20px;margin-bottom:20px;box-shadow:0 2px 4px rgba(0,0,0,0.1)}"
           ".summary p{font-size:16px;margin:8px 0}"
           ".pass{color:#28a745;font-weight:bold}.fail{color:#dc3545;font-weight:bold}"
           ".module{margin-bottom:24px}"
           ".module h2{background:#e9ecef;padding:10px 16px;border-radius:6px;font-size:16px;margin:0 0 8px 0}"
           ".module h2 .summary-line{font-weight:normal;font-size:13px;float:right}"
           "table{width:100%;border-collapse:collapse;background:#fff;border-radius:6px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08)}"
           "th{background:#007bff;color:#fff;padding:8px 14px;text-align:left;font-weight:600;font-size:13px}"
           "td{padding:7px 14px;border-bottom:1px solid #eee;font-size:13px}"
           "tr:hover{background:#f0f7ff}"
           "tr.failed-row{background:#fff0f0}"
           ".badge{display:inline-block;padding:1px 8px;border-radius:10px;color:#fff;font-size:11px;font-weight:bold}"
           ".badge-pass{background:#28a745}.badge-fail{background:#dc3545}"
           ".duration{color:#999;font-size:11px}"
           ".name-cn{font-size:14px;font-weight:500}.name-en{color:#999;font-size:11px;margin-top:1px}"
           ".params{color:#6c757d;font-size:11px;margin-left:4px}"
           ".error-detail{color:#dc3545;font-size:12px;margin:4px 0 0 20px;white-space:pre-wrap}"
           ".footer{margin-top:24px;color:#888;font-size:13px;text-align:center}")

    html_parts = [
        "<!DOCTYPE html>",
        '<html lang="zh-CN">',
        "<head>",
        '<meta charset="UTF-8">',
        "<title>BackgroundControl 测试报告</title>",
        f"<style>{CSS}</style>",
        "</head><body>",
        "<h1>BackgroundControl 测试报告</h1>",
        '<div class="summary">',
        f"<p><strong>总计:</strong> {total}",
        f' &nbsp;&nbsp; <span class="pass">通过: {passed}</span>',
        f' &nbsp;&nbsp; <span class="fail">失败: {failed}</span>',
        f" &nbsp;&nbsp; 跳过: {skipped}</p></div>",
    ]

    for cls in sorted(groups):
        tests = groups[cls]
        g_total = len(tests)
        g_failed = sum(1 for t in tests if t["outcome"] != "Passed")
        badge_cls = "badge-fail" if g_failed > 0 else "badge-pass"
        badge_txt = f"{g_failed} 失败" if g_failed > 0 else "通过"

        html_parts.append(f'<div class="module">'
                          f'<h2>{cls} <span class="summary-line">'
                          f'<span class="badge {badge_cls}">{badge_txt}</span> {g_total} 个测试</span></h2>')
        html_parts.append("<table><thead><tr><th>测试名称</th><th>结果</th><th>耗时</th></tr></thead><tbody>")

        for t in tests:
            row_cls = ' class="failed-row"' if t["outcome"] != "Passed" else ""
            badge_mark = '<span class="badge badge-pass">通过</span>' if t["outcome"] == "Passed" else '<span class="badge badge-fail">失败</span>'
            d = t["duration"]
            dur_str = f"{d/1000:.1f}s" if d >= 1000 else f"{d}ms"
            err = html.escape(t["error"]) if t["error"] else ""
            err_html = f'<div class="error-detail">{err}</div>' if err else ""
            name_line = f'{t["name"]}<span class="params">{t["params"]}</span>' if t["params"] else t["name"]

            html_parts.append(
                f'<tr{row_cls}><td>'
                f'<div class="name-cn">{t["name_cn"]}</div>'
                f'<div class="name-en">{name_line}</div>'
                f'{err_html}</td><td>{badge_mark}</td>'
                f'<td><span class="duration">{dur_str}</span></td></tr>')

        html_parts.append("</tbody></table></div>")

    html_parts.append(f'<div class="footer">生成时间: {ts} | BackgroundControl.Tests | {passed}/{total} 通过</div>')
    html_parts.append("</body></html>")

    html_text = "\n".join(html_parts)
    HTML_FILE.write_bytes(html_text.encode("utf-8"))
    return HTML_FILE

if __name__ == "__main__":
    no_html = "--no-html" in sys.argv
    ok = run_tests()
    if not ok:
        sys.exit(1)
    if no_html:
        print(f"=== Done ===\nReport: {TRX_FILE}")
    else:
        html = build_report()
        print(f"=== Done ===\nHTML report: {html}")
        try:
            os.startfile(html)
        except Exception:
            pass
