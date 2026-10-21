using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace FilmEcho {
public sealed class RealVerificationForm : Form {
    readonly string root=Application.StartupPath;
    readonly StringBuilder report=new StringBuilder(); int passes,failures;
    public RealVerificationForm() { Opacity=0; ShowInTaskbar=false; Size=new Size(800,500); Shown+=async(s,e)=> { try { await Run(); } catch(Exception ex) { Check(false,"异常："+ex); } finally { report.AppendLine("PASS="+passes+" FAIL="+failures); Write(); Environment.ExitCode=failures==0?0:1; Close(); } }; }
    void Write() { File.WriteAllText(Path.Combine(root,"verification","real-movies.txt"),report.ToString(),Encoding.UTF8); }
    void Check(bool ok,string text) { if(ok) passes++; else failures++; report.AppendLine((ok?"PASS: ":"FAIL: ")+text); Write(); }
    string HashFile(string p) { using(var f=File.OpenRead(p)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(f)); }
    async Task WaitSeek(MpvClient c,double target) {
        await c.Command("seek",target,"absolute+exact");
        await c.WaitForVideo();
        for(int n=0;n<40;n++) { await Task.Delay(50); if(Math.Abs(Json.Num(await c.Get("time-pos"))-target)<0.1) return; }
        throw new IOException("跳转后没有到达目标时间");
    }
    async Task Run() {
        var files=Directory.GetFiles(@"E:\英语电影","*",SearchOption.AllDirectories).Where(x=>new[]{".mp4",".mkv"}.Contains(Path.GetExtension(x).ToLowerInvariant())).OrderBy(x=>x).ToList();
        report.AppendLine("只读用户电影 · 软件解码 · 每片 20 次跳转/暂停播放 · 同一帧前后 SHA256 对比"); Write();
        foreach(string file in files) {
            string name=Path.GetFileName(file),key=Store.Key(file); report.AppendLine("\r\nMOVIE: "+file); Write();
            using(var client=new MpvClient()) {
                var panel=new Panel { Dock=DockStyle.Fill }; Controls.Add(panel); panel.CreateControl();
                try {
                    var loaded=new TaskCompletionSource<bool>(); client.Event+=(ev,d)=> { if(ev=="file-loaded") loaded.TrySetResult(true); if(ev=="end-file" && Json.Str(Json.Field(d,"reason"))=="error") loaded.TrySetException(new IOException("电影加载错误")); };
                    string log=Path.Combine(root,"verification","real-"+key+".log");
                    await client.Start(Path.Combine(root,"vendor","mpv","mpv.exe"),panel.Handle,log,Path.Combine(root,"source","input.conf"));
                    await client.Set("pause",true); await client.Command("loadfile",file,"replace");
                    if(await Task.WhenAny(loaded.Task,Task.Delay(20000))!=loaded.Task) throw new TimeoutException("电影加载超时"); await loaded.Task;
                    await WaitSeek(client,300.5);
                    Check(Json.Str(await client.Get("hwdec-current"))=="no","软件解码 / "+name);
                    var tracks=await client.Get("track-list") as object[];
                    report.AppendLine("TRACKS: "+Json.Encode(tracks)); Write();
                    string before=Path.Combine(root,"verification","frame-"+key+"-before.png"),after=Path.Combine(root,"verification","frame-"+key+"-after.png");
                    await client.Command("screenshot-to-file",before,"video");
                    bool positionsOkay=true;
                    for(int i=0;i<20;i++) {
                        double target=280+(i%9)*5;
                        await WaitSeek(client,target);
                        await client.Set("pause",false); await Task.Delay(100); await client.Set("pause",true);
                        double actual=Json.Num(await client.Get("time-pos")); if(Math.Abs(actual-target)>1.2) positionsOkay=false;
                        if(i%5==0) { report.AppendLine("progress "+(i+1)+"/20"); Write(); }
                    }
                    Check(positionsOkay,"20 次连续跳转与暂停播放，时间位置正常 / "+name);
                    await WaitSeek(client,300.5); await client.Command("screenshot-to-file",after,"video");
                    Check(HashFile(before)==HashFile(after),"反复跳转后，相同时间画面逐字节一致 / "+name);
                    report.AppendLine("FRAME_SHA256: "+HashFile(after)); Write();
                    client.Dispose();
                    string logText=File.ReadAllText(log);
                    Check(!System.Text.RegularExpressions.Regex.IsMatch(logText,@"\[e\]\[ffmpeg/video\]"),"测试片段无视频解码错误日志 / "+name);
                } catch(Exception ex) { Check(false,name+"："+ex.Message); }
                finally { Controls.Remove(panel); panel.Dispose(); }
            }
        }
    }
}
}
