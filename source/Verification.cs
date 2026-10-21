using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
public sealed class VerificationForm : Form {
    readonly StringBuilder report=new StringBuilder();
    readonly string root=Application.StartupPath;
    int passes,failures;
    public VerificationForm() {
        Text="FilmEcho 自动验证"; Size=new Size(800,520); ShowInTaskbar=false; Opacity=0;
        Shown+=async(s,e)=> { try { await Run(); } catch(Exception ex) { Check(false,"未处理异常："+ex); } finally { File.WriteAllText(Path.Combine(root,"verification","results.txt"),report.ToString()+"\r\nPASS="+passes+" FAIL="+failures,Encoding.UTF8); Environment.ExitCode=failures==0?0:1; Close(); } };
    }
    void Check(bool condition,string name) { if(condition) passes++; else failures++; report.AppendLine((condition?"PASS: ":"FAIL: ")+name); File.WriteAllText(Path.Combine(root,"verification","progress.txt"),report.ToString(),Encoding.UTF8); }
    async Task Run() {
        var store=new Store(Path.Combine(root,"verification","data-"+Guid.NewGuid().ToString("N")));
        string sample=Path.Combine(root,"verification","sample.mkv");
        var m=new MovieData { MoviePath=sample,Position=4.125 };
        m.Words.Add(new WordRecord { Word="hello",Sentence="Hello, world! 你好世界",Seconds=4.125,Meaning="你好",Note="测试\"逗号,换行\r\n保存" });
        store.SaveMovie(m); m.Position=5; store.SaveMovie(m);
        var restored=store.LoadMovie(sample);
        Check(restored.Words.Count==1 && restored.Words[0].Sentence==m.Words[0].Sentence && restored.Words[0].Note==m.Words[0].Note,"生词、双语原句、时间和笔记 SQLite 往返保存");
        Check(File.Exists(store.DatabaseFile) && store.IntegrityCheck()=="ok","SQLite 数据库已创建且完整性检查通过");
        Check(Store.Csv(m).Contains("测试\"\"逗号,换行") && Store.Csv(m).Contains("你好世界"),"CSV 中文、逗号、引号和换行转义");
        string other=Path.Combine(root,"verification","other.mp4"); Check(store.LoadMovie(other).Words.Count==0 && Store.Key(sample)!=Store.Key(other),"生词记录按完整电影路径隔离");
        string broken=store.MovieFile(other); File.WriteAllText(broken,"{broken"); bool protectedFile=false;
        try { store.LoadMovie(other); } catch(IOException) { protectedFile=File.ReadAllText(broken)=="{broken"; }
        Check(protectedFile,"损坏记录被拒绝加载，原文件未覆盖");
        using(var client=new MpvClient()) {
            var panel=new Panel { Dock=DockStyle.Fill }; Controls.Add(panel); panel.CreateControl();
            var loaded=new TaskCompletionSource<bool>(); client.Event+=(name,data)=> { if(name=="file-loaded") loaded.TrySetResult(true); };
            await client.Start(Path.Combine(root,"vendor","mpv","mpv.exe"),panel.Handle,Path.Combine(root,"verification","mpv.log"),Path.Combine(root,"source","input.conf"));
            await client.Set("pause",true); await client.Command("loadfile",sample,"replace");
            Check(await Task.WhenAny(loaded.Task,Task.Delay(15000))==loaded.Task,"嵌入 WinForms 窗口，加载 H.264 + 双字幕轨 + 音频测试片");
            await Task.Delay(450);
            Check(Json.Str(await client.Get("hwdec-current"))=="no","实际播放使用软件解码");
            Check(Json.Num(await client.Get("duration"))>=29,"正确读取总时长");
            var tracks=await client.Get("track-list") as object[];
            Check(tracks!=null && tracks.Count(x=>Json.Str(Json.Field(x,"type"))=="sub")==2,"读取两条内封字幕轨");
            var subs=tracks.Where(x=>Json.Str(Json.Field(x,"type"))=="sub").ToList();
            await client.Set("sid",Json.Field(subs[0],"id")); await client.Set("secondary-sid",Json.Field(subs[1],"id"));
            await client.Command("seek",4.5,"absolute+exact"); await Task.Delay(250);
            string caption=Json.Str(await client.Get("sub-text")),second=Json.Str(await client.Get("secondary-sub-text"));
            Check(caption.Contains("Hello") && second.Contains("你好"),"英语和中文字幕同时显示且可取得原句文字");
            Check(Math.Abs(Json.Num(await client.Get("time-pos"))-4.5)<0.12,"暂停状态下精确定位到 4.5 秒");
            int events=0; bool stressOkay=true;
            for(int i=0;i<50;i++) {
                double target=3+(i%7)*3;
                await client.Command("seek",target,"absolute+exact");
                await client.Set("pause",false); await Task.Delay(90); await client.Set("pause",true);
                double actual=Json.Num(await client.Get("time-pos")); if(Math.Abs(actual-target)>0.7) stressOkay=false;
                events++;
            }
            Check(stressOkay && events==50,"50 次快速精确跳转 + 暂停/播放，时间定位无失控");
            await client.Command("seek",12,"absolute+exact"); await Task.Delay(200); await client.Command("seek",-3,"relative+exact"); await Task.Delay(200);
            Check(Math.Abs(Json.Num(await client.Get("time-pos"))-9)<0.15,"倒退 3 秒精确到 9 秒");
            await client.Command("seek",5,"relative+exact"); await Task.Delay(200);
            Check(Math.Abs(Json.Num(await client.Get("time-pos"))-14)<0.15,"快进 5 秒精确到 14 秒");
            await client.Set("speed",0.75); Check(Math.Abs(Json.Num(await client.Get("speed"))-0.75)<0.001,"0.75 倍速控制");
            await client.Set("volume",65); Check(Math.Abs(Json.Num(await client.Get("volume"))-65)<0.001,"音量控制");
            await client.Command("seek",4.5,"absolute+exact"); await Task.Delay(250);
            string shot=Path.Combine(root,"verification","sample-frame.png"); await client.Command("screenshot-to-file",shot,"subtitles");
            Check(File.Exists(shot) && new FileInfo(shot).Length>1000,"双字幕画面截图可生成");
            await client.Command("quit");
        }
        Check(!File.ReadAllText(Path.Combine(root,"verification","mpv.log")).Contains("Error while decoding"),"合成片快速跳转测试日志无解码错误");
    }
}
}
