using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FilmEcho {
internal static class DefaultBrowser {
    [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]
    static extern uint AssocQueryString(uint flags,uint kind,string association,string verb,StringBuilder output,ref uint length);
    public static ProcessStartInfo StartInfo() {
        const uint protocol=0x1000; // ASSOCF_IS_PROTOCOL uses the current user's choice.
        uint length=0; AssocQueryString(protocol,2,"https","open",null,ref length);
        if(length<2 || length>32768) throw new IOException("请先在 Windows 设置中选择默认浏览器。");
        var text=new StringBuilder((int)length);
        if(AssocQueryString(protocol,2,"https","open",text,ref length)!=0) throw new IOException("无法读取 Windows 默认浏览器设置。");
        string executable=text.ToString().Trim('"');
        if(!Path.IsPathRooted(executable) || !File.Exists(executable) || !executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(executable),"rundll32.exe",StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(executable),"ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)) throw new IOException("默认浏览器不支持直接启动，请在 Windows 设置中重新选择。");
        return new ProcessStartInfo(executable) { UseShellExecute=true,Arguments="" };
    }
    public static void Open(Action<ProcessStartInfo> launch=null) {
        var info=StartInfo(); if(launch!=null) launch(info); else using(var process=Process.Start(info)) { }
    }
}
internal static class OcrRuntime {
    public static string Script(string root,string output) {
#if PUBLIC_RELEASE
        Directory.CreateDirectory(output);
        string path=Path.Combine(output,"ocr-runtime.ps1");
        using(var stream=typeof(OcrRuntime).Assembly.GetManifestResourceStream("FilmEcho.OcrScript")) {
            if(stream==null) throw new IOException("字幕识别组件缺失。");
            using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                string text=reader.ReadToEnd(); if(!File.Exists(path) || File.ReadAllText(path,Encoding.UTF8)!=text) File.WriteAllText(path,text,new UTF8Encoding(true));
            }
        }
        return path;
#else
        return Path.Combine(root,"source","ocr.ps1");
#endif
    }
}
public sealed partial class MainForm {
    internal Action<ProcessStartInfo> BrowserLauncher;
    void OpenDefaultBrowser() {
        try { DefaultBrowser.Open(BrowserLauncher); Status("默认浏览器已打开"); }
        catch(Exception ex) { Status("默认浏览器无法打开："+ex.Message,true); }
    }
}
}
