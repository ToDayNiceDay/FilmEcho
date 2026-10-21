using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FilmEcho {
sealed class WelcomeVerificationForm : Form {
    readonly StringBuilder report=new StringBuilder();
    readonly string root=Application.StartupPath;
    int failures;
    public WelcomeVerificationForm() {
        ShowInTaskbar=false; Opacity=0; Size=new Size(100,100);
        Shown+=(s,e)=> { try { Verify(); } catch(Exception ex) { Check(false,"欢迎页验证异常："+ex); } finally { report.AppendLine("FAIL="+failures); File.WriteAllText(Path.Combine(root,"verification","welcome-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failures==0?0:1; Close(); } };
    }
    void Check(bool ok,string description) { report.AppendLine((ok?"PASS: ":"FAIL: ")+description); if(!ok) failures++; }
    static AppSettings ReadSettings(string root) { return Json.Decode<AppSettings>(File.ReadAllText(Path.Combine(root,"data","settings.json"),Encoding.UTF8)); }
    static DialogResult ShowAndAct(WelcomeDialog dialog,Action<WelcomeDialog> act) {
        dialog.StartPosition=FormStartPosition.Manual; dialog.Location=new Point(-20000,-20000);
        using(var timer=new Timer { Interval=180 }) { timer.Tick+=(s,e)=> { timer.Stop(); try { act(dialog); } catch { dialog.Close(); throw; } }; timer.Start(); return dialog.ShowDialog(); }
    }
    static void SavePreview(WelcomeDialog dialog,string path) {
        using(var image=new Bitmap(dialog.Width,dialog.Height)) {
            dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
            using(var graphics=Graphics.FromImage(image)) foreach(var text in new[]{dialog.Introduction,dialog.Content}) {
                Point location=dialog.PointToClient(text.PointToScreen(Point.Empty));
                location.Offset((dialog.Width-dialog.ClientSize.Width)/2,dialog.Height-dialog.ClientSize.Height-(dialog.Width-dialog.ClientSize.Width)/2);
                using(var brush=new SolidBrush(text.BackColor)) graphics.FillRectangle(brush,new Rectangle(location,text.Size));
                using(var brush=new SolidBrush(text.ForeColor)) graphics.DrawString(text.Text,text.Font,brush,new RectangleF(location.X+2,location.Y+2,text.Width-20,text.Height-4));
            }
            image.Save(path,ImageFormat.Png);
        }
    }
    void Verify() {
        string testRoot=Path.Combine(root,"verification","welcome-data-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testRoot);
        int presented=0;
        bool entered=StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> {
            presented++;
            Check(page.Visible && !Application.OpenForms.Cast<Form>().Any(form=>form is MainForm) && !File.Exists(Path.Combine(testRoot,"data","learning.sqlite3")),"主程序尚未创建时先实际打开欢迎页，不提前启动学习数据库");
            Check(!page.SkipNextTime.Checked && page.Content.ReadOnly && page.Introduction.ReadOnly,"首次默认显示欢迎页，复选框未勾选，说明文本只读");
            Check(page.Introduction.Text==WelcomeDialog.PublisherIntroduction && page.Introduction.Text.Contains("2252521563@qq.com"),"完整显示指定的蔚蓝时刻介绍和留言邮箱，中文原文逐字一致");
            Check(page.Content.Text==HelpDialog.HelpText(false) && page.Content.Text.Contains("播放快捷键") && page.Text.Contains(AppInfo.Version),"欢迎页复用完整使用帮助、快捷键及当前版本号");
            Check(page.BackColor.ToArgb()==ThemePalette.For(true).Background.ToArgb() && page.Content.BackColor.ToArgb()==ThemePalette.For(true).Field.ToArgb(),"首次欢迎页默认深色，与主程序配色一致");
            Check(page.RectangleToScreen(page.ClientRectangle).Contains(page.SkipNextTime.RectangleToScreen(page.SkipNextTime.ClientRectangle)) && Math.Abs(page.SkipNextTime.PointToScreen(Point.Empty).Y+page.SkipNextTime.Height/2-page.EnterPlayer.PointToScreen(Point.Empty).Y-page.EnterPlayer.Height/2)<15,"复选框完整位于客户区，文字与进入按钮同排且垂直对齐");
            SavePreview(page,Path.Combine(root,"verification","interface-welcome-dark.png"));
            page.EnterPlayer.PerformClick();
        }));
        Check(entered && presented==1,"点击进入播放器后欢迎页关闭，启动门禁允许继续打开主界面");
        bool again=StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> { presented++; page.ExitButton.PerformClick(); }));
        Check(!again && presented==2,"未勾选时再次启动仍显示，点击退出不进入主界面");
        entered=StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> { page.SkipNextTime.Checked=true; page.EnterPlayer.PerformClick(); }));
        Check(entered && ReadSettings(testRoot).SkipWelcome,"勾选后点击进入：首次创建设置目录并保存下次不再显示");
        bool skipped=StartupWelcome.ShowIfNeeded(testRoot,dialog=> { throw new InvalidOperationException("Unexpected welcome"); });
        Check(skipped,"重新读取磁盘设置后直接通过，不构造或显示欢迎页");
        var original=new AppSettings { LastMovie="E:\\电影\\片源.mkv",LibraryFolder="E:\\电影",JumpSeconds=5,Volume=67,Speed=.85,Theme="light",Language="en",SkipWelcome=false };
        string settingsFile=Path.Combine(testRoot,"data","settings.json"); Store.Write(settingsFile,original);
        entered=StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> {
            Check(page.Content.Text.Contains("Start shadowing") && page.EnterPlayer.Text=="Enter player" && page.SkipNextTime.Text=="Don't show this page again" && page.Introduction.Text.Contains("2252521563@qq.com"),"英文帮助、按钮与复选框文案完整，介绍保留正确邮箱");
            Check(page.Content.BackColor.ToArgb()==ThemePalette.For(false).Field.ToArgb(),"欢迎页读取并沿用已保存的浅色主题");
            SavePreview(page,Path.Combine(root,"verification","interface-welcome-light-english.png"));
            page.Size=page.MinimumSize; page.PerformLayout();
            Check(page.SkipNextTime.Right<page.EnterPlayer.PointToScreen(Point.Empty).X-page.SkipNextTime.Parent.PointToScreen(Point.Empty).X && page.Content.Height>130,"640×620 小窗口下复选框与操作按钮不重叠，帮助区域可滚动");
            Check(page.RectangleToScreen(page.ClientRectangle).Contains(page.SkipNextTime.RectangleToScreen(page.SkipNextTime.ClientRectangle)),"最小窗口下复选框未被页底裁切");
            SavePreview(page,Path.Combine(root,"verification","interface-welcome-small.png"));
            page.SkipNextTime.Checked=true; page.EnterPlayer.PerformClick();
        }));
        var stored=ReadSettings(testRoot); stored.SkipWelcome=false;
        Check(entered && Json.Encode(stored)==Json.Encode(original) && File.Exists(settingsFile+".bak"),"只修改欢迎页偏好，电影路径、主题、语言、音量、倍速等设置保持并生成备份");
        Store.Write(settingsFile,original);
        bool closed=StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> { page.SkipNextTime.Checked=true; page.Close(); }));
        Check(!closed && !ReadSettings(testRoot).SkipWelcome,"关闭欢迎窗口取消启动，未确认的勾选不会保存");
        using(var page=new WelcomeDialog(ThemePalette.For(true),false,null,skip=> { throw new IOException("测试写入失败"); })) {
            var result=ShowAndAct(page,dialog=> {
                dialog.SkipNextTime.Checked=true; dialog.EnterPlayer.PerformClick();
                Check(dialog.Visible && dialog.DialogResult==DialogResult.None && dialog.ErrorLabel.Text.Contains("测试写入失败"),"偏好写入失败时保持欢迎页并显示错误，不进入主界面"); dialog.ExitButton.PerformClick();
            });
            Check(result==DialogResult.Cancel,"保存失败后可正常退出欢迎页");
        }
        string retryRoot=Path.Combine(testRoot,"retry"); Directory.CreateDirectory(retryRoot);
        string blocker=Path.Combine(retryRoot,"data"); File.WriteAllText(blocker,"fixture prevents directory creation");
        bool retried=StartupWelcome.ShowIfNeeded(retryRoot,dialog=>ShowAndAct(dialog,page=> {
            page.SkipNextTime.Checked=true; page.EnterPlayer.PerformClick();
            Check(page.Visible && page.DialogResult==DialogResult.None && page.ErrorLabel.Text.Length>0,"真实设置目录写入失败时不误判已保存，欢迎页保持打开");
            File.Delete(blocker); page.EnterPlayer.PerformClick();
        }));
        Check(retried && ReadSettings(retryRoot).SkipWelcome,"解除写入故障后同一欢迎页可重试，偏好实际落盘后才进入");
        File.WriteAllText(settingsFile,"{broken",Encoding.UTF8); bool refused=false;
        try { StartupWelcome.ShowIfNeeded(testRoot,dialog=>DialogResult.OK); } catch { refused=true; }
        Check(refused && File.ReadAllText(settingsFile)=="{broken","损坏设置不被覆盖或自动重置");
        var legacy=Json.Map(Json.Parse(Json.Encode(original))); legacy.Remove("SkipWelcome"); Store.Write(settingsFile,legacy);
        bool legacyShown=false; StartupWelcome.ShowIfNeeded(testRoot,dialog=>ShowAndAct(dialog,page=> { legacyShown=!page.SkipNextTime.Checked; page.ExitButton.PerformClick(); }));
        Check(legacyShown && !Json.Map(Json.Parse(File.ReadAllText(settingsFile))).ContainsKey("SkipWelcome"),"旧版设置缺少字段时默认显示，退出后旧设置文件保持原结构");
    }
}
}
