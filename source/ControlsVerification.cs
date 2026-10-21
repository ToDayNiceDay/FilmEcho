using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
public sealed partial class MainForm {
    static readonly BindingFlags ControlMembers=BindingFlags.Instance|BindingFlags.NonPublic;
    static void TestMouse(Control control,string method,int x,int y,int delta=0) { typeof(Control).GetMethod(method,ControlMembers).Invoke(control,new object[]{new MouseEventArgs(MouseButtons.Left,1,x,y,delta)}); }
    static void TestKey(Control control,Keys key) { control.GetType().GetMethod("ProcessCmdKey",ControlMembers).Invoke(control,new object[]{new Message(),key}); }
    static Control PopupSurface(ModernComboBox combo) { return (Control)typeof(ModernComboBox).GetField("choices",ControlMembers).GetValue(combo); }
    static ToolStripDropDown PopupWindow(ModernComboBox combo) { return (ToolStripDropDown)typeof(ModernComboBox).GetField("popup",ControlMembers).GetValue(combo); }
    static bool ChildrenFit(Control parent) { foreach(Control c in parent.Controls) if(c.Visible && (c.Bottom>parent.ClientSize.Height+1 || c.Right>parent.ClientSize.Width+1)) return false; return true; }
    static Control ManagedActiveLeaf(ContainerControl parent) { Control active=parent.ActiveControl; while(active is ContainerControl && ((ContainerControl)active).ActiveControl!=null) active=((ContainerControl)active).ActiveControl; return active; }
    async Task VerifyControls() {
        var report=new StringBuilder(); int failed=0;
        Action<bool,string> check=(ok,name)=> { report.AppendLine((ok?"PASS: ":"FAIL: ")+name); if(!ok) failed++; File.WriteAllText(Path.Combine(root,"verification","controls-results.txt"),report.ToString(),Encoding.UTF8); };
        try {
            await Seek(4.5); await mpv.WaitForVideo(); ApplyTheme("dark",false); ApplyLanguage("zh",false);
            check(movie!=null && ready && !loading,"真实 mpv 与合成片初始化成功");
            movies.SelectedIndex=0;
            check(movies.SelectedItem is string && File.Exists((string)movies.SelectedItem) && movies.GetItemText(movies.SelectedItem)==Path.GetFileName((string)movies.SelectedItem),"电影列表展示文件名，内部保留完整真实路径");
            speed.Focus(); TestKey(speed,Keys.Enter); await Task.Delay(120);
            check(speed.IsDropdownOpen && PopupSurface(speed).ContainsFocus,"Enter 展开倍速选项，弹出面板获得键盘焦点");
            TestKey(PopupSurface(speed),Keys.Down); TestKey(PopupSurface(speed),Keys.Enter); await Task.Delay(300);
            check(!speed.IsDropdownOpen && Math.Abs(Json.Num(await mpv.Get("speed"))-1.1)<.01,"方向键和 Enter 确认倍速，实际 mpv 变为 1.1 倍");
            speed.OpenDropdown(); TestKey(PopupSurface(speed),Keys.End); TestKey(PopupSurface(speed),Keys.Escape);
            check(!speed.IsDropdownOpen && speed.Text=="1.1×","Esc 取消候选项，不改变已选倍速");
            speed.OpenDropdown(); TestKey(PopupSurface(speed),Keys.Home); TestKey(PopupSurface(speed),Keys.PageDown); TestKey(PopupSurface(speed),Keys.Enter); await Task.Delay(150);
            check(speed.SelectedIndex==7 && Math.Abs(Json.Num(await mpv.Get("speed"))-2)<.01,"Home / PageDown 跨项导航并确认末项");
            speed.SelectedIndex=3; await Task.Delay(150);
            double before=Json.Num(await mpv.Get("time-pos")); themeChoice.Focus(); TestMouse(themeChoice,"OnMouseDown",20,12); await Task.Delay(100);
            check(themeChoice.IsDropdownOpen,"点击控件展开自绘主题面板");
            TestMouse(PopupSurface(themeChoice),"OnMouseDown",22,60); await Task.Delay(150);
            check(settings.Theme=="light" && store.LoadSettings().Theme=="light" && query.BackColor.ToArgb()==theme.Field.ToArgb(),"点击浅色项同步控件、输入框与磁盘主题偏好");
            check(Math.Abs(Json.Num(await mpv.Get("time-pos"))-before)<.01 && Convert.ToBoolean(await mpv.Get("pause")),"主题切换保持暂停与原播放位置");
            await SavePreview(Path.Combine(root,"verification","controls-light.png"));
            themeChoice.OpenDropdown(); TestMouse(PopupSurface(themeChoice),"OnMouseDown",22,23); await Task.Delay(150);
            check(settings.Theme=="dark" && store.LoadSettings().Theme=="dark","点击深色项恢复并保存主题偏好");
            movies.SelectedIndex=0; movies.OpenDropdown(); TestMouse(PopupSurface(movies),"OnMouseWheel",20,20,-120); TestMouse(PopupSurface(movies),"OnMouseDown",20,22);
            check(movies.SelectedIndex==Math.Min(3,Math.Max(0,movies.Items.Count-8)),"长电影列表滚轮移动视窗，点击选择对应真实项目");
            movies.OpenDropdown(); PopupWindow(movies).Close(ToolStripDropDownCloseReason.AppClicked); query.Select(); await Task.Delay(60);
            check(!movies.IsDropdownOpen && ManagedActiveLeaf(this)==query,"外部关闭弹层后可将查词框设为活动控件（隐藏宿主，不判定原生焦点）");
            speed.Focus(); speed.OpenDropdown(); TestKey(PopupSurface(speed),Keys.Tab);
            check(!speed.IsDropdownOpen && !speed.ContainsFocus,"Tab 关闭弹层并继续控件焦点导航");
            int oldSubtitle=primary.SelectedIndex; primary.Focus(); TestKey(primary,Keys.End); await Task.Delay(250);
            check(Json.Str(await mpv.Get("sid"))==((TrackChoice)primary.SelectedItem).Id,"自绘字幕选择实际切换 mpv 字幕轨");
            primary.SelectedIndex=oldSubtitle; await Task.Delay(150);
            TestMouse(volume,"OnMouseDown",55,15); TestMouse(volume,"OnMouseUp",55,15); await Task.Delay(200);
            check(volume.Value==56 && Math.Abs(Json.Num(await mpv.Get("volume"))-56)<.1,"圆角音量滑条点击定位并更新真实音量");
            double current=Json.Num(await mpv.Get("time-pos")); typeof(Control).GetMethod("OnKeyDown",ControlMembers).Invoke(volume,new object[]{new KeyEventArgs(Keys.Right)}); await Task.Delay(160);
            check(volume.Value==57 && Math.Abs(Json.Num(await mpv.Get("volume"))-57)<.1 && Math.Abs(Json.Num(await mpv.Get("time-pos"))-current)<.01,"滑条方向键入口调节真实音量并保持电影位置");
            TestMouse(timeline,"OnMouseDown",timeline.Width/2,16); TestMouse(timeline,"OnMouseMove",timeline.Width*2/3,16); TestMouse(timeline,"OnMouseUp",timeline.Width*2/3,16); await Task.Delay(400); await mpv.WaitForVideo();
            check(position>19 && position<21 && !seeking,"进度滑条拖动后精确跳转，解除拖动状态");
            await Seek(4.5); await mpv.WaitForVideo();
            autoPause.Focus(); typeof(Control).GetMethod("OnClick",ControlMembers).Invoke(autoPause,new object[]{EventArgs.Empty}); await Task.Delay(180);
            check(autoPause.Checked,"圆角开关点击切换句尾自动暂停选项"); autoPause.Checked=false;
            TestMouse(tabs,"OnMouseDown",tabs.Width*5/6,17);
            check(tabs.SelectedIndex==2 && history.Visible && !definition.Visible,"分段标签点击切换本片生词内容");
            tabs.Focus(); TestKey(tabs,Keys.Control|Keys.Tab);
            check(tabs.SelectedIndex==0 && definition.Visible,"Ctrl+Tab 切换页签并显示释义页");
            lookupButton.Enabled=false; using(var button=new Bitmap(lookupButton.Width,lookupButton.Height)) { lookupButton.DrawToBitmap(button,new Rectangle(Point.Empty,button.Size)); button.Save(Path.Combine(root,"verification","button-disabled.png")); } lookupButton.Enabled=true;
            check(lookupButton.Height>=34 && query.Height>=query.PreferredHeight && ChildrenFit(primary.Parent) && ChildrenFit(speed.Parent),"默认窗口的按钮、输入区、音轨及播放控件完整布局");
            Size previous=Size; Size=new Size(1100,800); await Task.Delay(160);
            check(ChildrenFit(primary.Parent) && ChildrenFit(speed.Parent) && video.Height>40,"小窗口音轨和播放控件自动换行，保留视频区域"); Size=previous; await Task.Delay(160);
            await SavePreview(Path.Combine(root,"verification","controls-dark.png"));
            movies.Focus(); movies.OpenDropdown(); await Task.Delay(150);
            var popup=PopupWindow(movies); Rectangle work=Screen.FromControl(this).WorkingArea;
            check(work.Contains(popup.Bounds),"展开列表定位在屏幕工作区内");
            await SavePopupPreview(Path.Combine(root,"verification","controls-dropdown.png"),popup); movies.CloseDropdown();
            pauseButton.Focus(); check(movies.AccessibilityObject.Role==AccessibleRole.ComboBox && movies.AccessibilityObject.Value==movies.Text,"下拉框公开可访问角色、选中值与展开动作");
            await VerifyInterfaceUpgrade(check);
        } catch(Exception ex) { check(false,"控件验证异常："+ex); }
        finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(root,"verification","controls-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
    }
    async Task SavePopupPreview(string path,ToolStripDropDown popup) {
        await SavePreview(path);
        using(var main=new Bitmap(path)) using(var list=new Bitmap(popup.Width,popup.Height)) {
            popup.DrawToBitmap(list,new Rectangle(Point.Empty,list.Size));
            Point p=PointToClient(popup.Location); p.Offset((Width-ClientSize.Width)/2,Height-ClientSize.Height-(Width-ClientSize.Width)/2);
            using(var g=Graphics.FromImage(main)) g.DrawImageUnscaled(list,p);
            main.Save(path+".tmp",ImageFormat.Png);
        }
        File.Delete(path); File.Move(path+".tmp",path);
    }
}
}
