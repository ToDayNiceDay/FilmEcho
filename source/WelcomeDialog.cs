using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FilmEcho {
static class StartupWelcome {
    // Read just the JSON preference. The main form, SQLite, dictionary and mpv
    // are constructed only after the user enters the player.
    internal static bool ShowIfNeeded(string root,Func<WelcomeDialog,DialogResult> present=null) {
        string path=Path.Combine(root,"data","settings.json");
        var settings=File.Exists(path)?Json.Decode<AppSettings>(File.ReadAllText(path,Encoding.UTF8)):new AppSettings();
        if(settings==null) throw new IOException("设置文件内容无效，已保留原文件");
        if(settings.SkipWelcome) return true;
        Icon icon=null; try { icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        try {
            using(var welcome=new WelcomeDialog(ThemePalette.For(settings.Theme!="light"),settings.Language=="en",icon,skip=> {
                if(settings.SkipWelcome==skip) return;
                bool previous=settings.SkipWelcome; settings.SkipWelcome=skip;
                try { Directory.CreateDirectory(Path.GetDirectoryName(path)); Store.Write(path,settings); }
                catch { settings.SkipWelcome=previous; throw; }
            })) return present==null?welcome.ShowDialog()==DialogResult.OK:present(welcome)==DialogResult.OK;
        } finally { icon?.Dispose(); }
    }
}
sealed class WelcomeCheckBox : CheckBox {
    readonly ThemePalette palette;
    public WelcomeCheckBox(ThemePalette theme) {
        palette=theme; AutoSize=true; Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    public override Size GetPreferredSize(Size proposedSize) { var text=TextRenderer.MeasureText(Text,Font); return new Size(text.Width+30,Math.Max(32,text.Height+8)); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); float y=(Height-18)/2f;
        ModernPaint.Card(e.Graphics,new RectangleF(1,y,18,18),Checked?palette.Primary:palette.Field,Focused?palette.Accent:palette.Border,4);
        if(Checked) using(var pen=new Pen(palette.PrimaryText,2)) { pen.StartCap=pen.EndCap=LineCap.Round; e.Graphics.DrawLines(pen,new[]{new PointF(5,y+9),new PointF(8,y+12),new PointF(15,y+5)}); }
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(28,0,Math.Max(0,Width-28),Height),palette.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
}
sealed class WelcomeDialog : Form {
    internal const string PublisherIntroduction="这里是「蔚蓝时刻」，分享我制作的软件、实用工具和开发过程。目前正在开发一款用于视频播放与英语学习的软件，未来也会陆续发布其他作品。欢迎体验、留言(2252521563@qq.com)提出改进建议。";
    readonly ThemePalette palette;
    readonly bool english;
    readonly Action<bool> savePreference;
    readonly TableLayoutPanel layout;
    internal readonly RichTextBox Introduction=new RichTextBox(),Content=new RichTextBox();
    internal readonly WelcomeCheckBox SkipNextTime;
    internal readonly ModernButton EnterPlayer,ExitButton;
    internal readonly Label ErrorLabel=new Label();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    public WelcomeDialog(ThemePalette theme,bool useEnglish,Icon icon,Action<bool> save) {
        palette=theme; english=useEnglish; savePreference=save;
        Text="蔚蓝时刻 · "+(english?"Welcome":"欢迎使用")+" · FilmEcho v"+AppInfo.Version; Icon=icon;
        Font=new Font("Microsoft YaHei UI",10); BackColor=theme.Background; ForeColor=theme.Text;
        Size=new Size(840,790); MinimumSize=new Size(640,620); StartPosition=FormStartPosition.CenterScreen; MinimizeBox=false; MaximizeBox=false;
        layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(22,18,22,16) };
        foreach(float height in new[]{72F,132F,36F}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,0)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text=(english?"蔚蓝时刻  /  Welcome":"蔚蓝时刻  /  欢迎使用")+"\nFilmEcho · "+(english?"Movie shadowing":"英语电影跟读")+" · v"+AppInfo.Version,Font=new Font(Font.FontFamily,15,FontStyle.Bold),ForeColor=theme.Accent,Dock=DockStyle.Fill,AutoEllipsis=true },0,0);
        ConfigureText(Introduction); Introduction.Text=english?"This is 蔚蓝时刻. I share the software and practical tools I make, along with my development process. I am currently developing an app for video playback and English learning, with more projects to come. You are welcome to try it and send suggestions to 2252521563@qq.com.":PublisherIntroduction;
        Introduction.ScrollBars=RichTextBoxScrollBars.Vertical;
        layout.Controls.Add(new ModernField(Introduction) { Dock=DockStyle.Fill,Palette=theme,Margin=new Padding(0,4,0,8) },0,1);
        layout.Controls.Add(new Label { Text=english?"How to use FilmEcho":"使用帮助",ForeColor=theme.Accent,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font,FontStyle.Bold) },0,2);
        ConfigureText(Content); Content.Text=HelpDialog.HelpText(english);
        layout.Controls.Add(new ModernField(Content) { Dock=DockStyle.Fill,Palette=theme,Margin=Padding.Empty },0,3);
        ErrorLabel.Dock=DockStyle.Fill; ErrorLabel.ForeColor=theme.Error; ErrorLabel.AutoEllipsis=true; ErrorLabel.TextAlign=ContentAlignment.MiddleLeft; layout.Controls.Add(ErrorLabel,0,4);
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(0,13,0,0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,242));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        SkipNextTime=new WelcomeCheckBox(theme) { Text=english?"Don't show this page again":"下次不再显示此页面",BackColor=theme.Background,AutoSize=false,Dock=DockStyle.Fill,Margin=Padding.Empty,TabIndex=0 };
        footer.Controls.Add(SkipNextTime,0,0);
        var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty };
        EnterPlayer=new ModernButton { Text=english?"Enter player":"进入播放器",Size=new Size(118,36),Palette=theme,BackColor=theme.Primary,ForeColor=theme.PrimaryText,Margin=new Padding(4,0,0,0),TabIndex=1 };
        ExitButton=new ModernButton { Text=english?"Exit":"退出",Size=new Size(106,36),Palette=theme,BackColor=theme.Button,ForeColor=theme.Text,Margin=new Padding(4,0,0,0),TabIndex=2,DialogResult=DialogResult.Cancel };
        EnterPlayer.Click+=(s,e)=> {
            try { savePreference(SkipNextTime.Checked); DialogResult=DialogResult.OK; }
            catch(Exception ex) { ErrorLabel.Text=(english?"Could not save the preference: ":"偏好保存失败：")+ex.Message; layout.RowStyles[4].Height=50; }
        };
        buttons.Controls.Add(EnterPlayer); buttons.Controls.Add(ExitButton); footer.Controls.Add(buttons,1,0); layout.Controls.Add(footer,0,5);
        AcceptButton=EnterPlayer; CancelButton=ExitButton;
    }
    void ConfigureText(RichTextBox box) {
        box.ReadOnly=true; box.BorderStyle=BorderStyle.None; box.BackColor=palette.Field; box.ForeColor=palette.Text; box.Font=Font; box.Dock=DockStyle.Fill; box.ScrollBars=RichTextBoxScrollBars.Vertical; box.DetectUrls=false; box.TabStop=false;
    }
    protected override void OnLoad(EventArgs e) {
        if(StartPosition==FormStartPosition.CenterScreen) {
            var work=Screen.FromControl(this).WorkingArea;
            Size=new Size(Math.Min(Width,Math.Max(MinimumSize.Width,work.Width-32)),Math.Min(Height,Math.Max(MinimumSize.Height,work.Height-32)));
        }
        base.OnLoad(e);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { int dark=palette.Background.GetBrightness()<.3?1:0; DwmSetWindowAttribute(Handle,20,ref dark,4); } catch { } }
}
}
