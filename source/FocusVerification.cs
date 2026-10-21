using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
static class FocusProbe {
    internal delegate bool WindowVisitor(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent,WindowVisitor visitor,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int size);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    internal static IntPtr FindPlayer(IntPtr parent) {
        IntPtr found=IntPtr.Zero;
        EnumChildWindows(parent,(window,data)=> { var name=new StringBuilder(256); GetClassName(window,name,name.Capacity); if(name.ToString().IndexOf("mpv",StringComparison.OrdinalIgnoreCase)>=0) found=window; return true; },IntPtr.Zero);
        return found;
    }
    internal static void Click(IntPtr window) {
        var point=new IntPtr((24<<16)|24);
        PostMessage(window,0x200,IntPtr.Zero,point); // WM_MOUSEMOVE
        PostMessage(window,0x201,new IntPtr(1),point); // WM_LBUTTONDOWN
        PostMessage(window,0x202,IntPtr.Zero,point); // WM_LBUTTONUP
    }
    internal static void Key(Keys key) {
        IntPtr target=GetFocus();
        PostMessage(target,0x100,new IntPtr((int)key),new IntPtr(1));
        PostMessage(target,0x101,new IntPtr((int)key),new IntPtr(unchecked((int)0xC0000001)));
    }
}
public sealed partial class MainForm {
    async Task VerifyVideoFocus() {
        var report=new StringBuilder(); int failed=0;
        Action<bool,string> check=(ok,name)=> { report.AppendLine((ok?"PASS: ":"FAIL: ")+name); if(!ok) failed++; File.WriteAllText(Path.Combine(root,"verification","focus-results.txt"),report.ToString(),Encoding.UTF8); };
        try {
            // Make our own HWND visible off screen, without foreground activation.
            // A SW_HIDE host cannot prove native focus with Control.Focused/GetFocus.
            WindowState=FormWindowState.Normal; Location=new Point(-20000,-20000);
            FocusProbe.ShowWindow(Handle,4); await Task.Delay(120);
            await mpv.Set("pause",true); await Seek(4.5); await mpv.WaitForVideo(); await Task.Delay(120);
            IntPtr child=FocusProbe.FindPlayer(video.Handle);
            check(child!=IntPtr.Zero && FocusProbe.IsWindowVisible(Handle),"找到真实 mpv 原生子窗口，验证宿主在屏幕外可见");
            if(child==IntPtr.Zero) throw new InvalidOperationException("Native mpv window not found");
            query.Text="focus gypqj draft"; note.Text="未保存笔记 / unsaved note"; query.Select(); query.Focus();
            check(FocusProbe.GetFocus()==query.Handle && query.Focused,"查词框先实际获得 Win32 键盘焦点（不是仅检查 ActiveControl）");
            query.Select(query.TextLength,0); FocusProbe.Key(Keys.Space); await Task.Delay(100);
            check(query.Text=="focus gypqj draft " && Convert.ToBoolean(await mpv.Get("pause")),"仍在输入框时空格用于输入文字，不误触电影播放");
            int transitions=0; bool last=true;
            Action<string,object> observer=(name,value)=> { if(name=="pause") { bool now=Convert.ToBoolean(value); if(now!=last) { transitions++; last=now; } } };
            mpv.PropertyChanged+=observer;
            try {
                FocusProbe.Click(child); await Task.Delay(450);
                check(FocusProbe.GetFocus()==video.Handle && video.Focused && !query.Focused && ManagedActiveLeaf(this)==video,"向真实 mpv 子窗口发送鼠标按下/抬起后，原生焦点与容器活动控件均转到视频宿主");
                check(!Convert.ToBoolean(await mpv.Get("pause")) && transitions==1,"一次视频点击只切换一次播放状态，暂停变播放");
                check(query.Text=="focus gypqj draft " && note.Text=="未保存笔记 / unsaved note","点击画面保留查词草稿与未保存笔记");
                FocusProbe.Key(Keys.Space); await Task.Delay(300);
                check(Convert.ToBoolean(await mpv.Get("pause")) && transitions==2,"视频焦点下的真实 WM_KEYDOWN 空格经宿主快捷键暂停电影");
            } finally { mpv.PropertyChanged-=observer; }
            double before=Json.Num(await mpv.Get("time-pos")); FocusProbe.Key(Keys.Right); await LookupUntil(()=>Math.Abs(position-before-settings.JumpSeconds)<.2);
            check(Math.Abs(Json.Num(await mpv.Get("time-pos"))-before-settings.JumpSeconds)<.2,"点击画面后原生方向键按配置步长精确快进");
            FocusProbe.Key(Keys.Left); await LookupUntil(()=>Math.Abs(position-before)<.2);
            check(Math.Abs(Json.Num(await mpv.Get("time-pos"))-before)<.2,"原生左方向键精确倒退，输入文字未被编辑");
            note.Select(); note.Focus(); check(FocusProbe.GetFocus()==note.Handle,"笔记框实际获得原生焦点");
            FocusProbe.Click(child); await Task.Delay(350);
            check(FocusProbe.GetFocus()==video.Handle && !note.Focused && !Convert.ToBoolean(await mpv.Get("pause")),"笔记输入后点击画面同样交回焦点并继续播放");
            await mpv.Set("pause",true); await Task.Delay(80); sentence.Select(); sentence.Focus();
            check(FocusProbe.GetFocus()==sentence.Handle,"原句框实际获得原生焦点");
            FocusProbe.Click(child); await Task.Delay(350);
            check(FocusProbe.GetFocus()==video.Handle && !sentence.Focused,"原句输入后点击画面同样恢复播放器焦点");
            await mpv.Set("pause",true); await Task.Delay(100);
            ApplyTheme("light",false); ApplyLanguage("en",false); query.Select(); query.Focus();
            FocusProbe.Click(child); await Task.Delay(350);
            check(FocusProbe.GetFocus()==video.Handle && EnglishUi && !Convert.ToBoolean(await mpv.Get("pause")),"英文浅色界面下视频点击焦点交接正常");
            // Keep fullscreen checks hidden so automated tests never cover the desktop.
            FocusProbe.ShowWindow(Handle,0);
            query.Select(); await mpv.Command("keypress","MBTN_LEFT_DBL"); await Task.Delay(200);
            check(fullscreen && ManagedActiveLeaf(this)==video,"真实 mpv 双击绑定进入全屏并交回活动控件");
            await mpv.Command("keypress","MBTN_LEFT_DBL"); await Task.Delay(200);
            check(!fullscreen,"双击绑定退出全屏，恢复窗口尺寸");
            ApplyTheme("dark",false); ApplyLanguage("zh",false); await mpv.Set("pause",true);
            query.Clear(); note.Clear(); await Seek(4.5); await mpv.WaitForVideo();
        } catch(Exception ex) { check(false,"焦点验证异常："+ex); }
        finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(root,"verification","focus-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
    }
}
}
