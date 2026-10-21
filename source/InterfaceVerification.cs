using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FilmEcho {
public sealed partial class MainForm {
    static string GlyphMask(Control editor,string path) {
        using(var bmp=new Bitmap(editor.Width,editor.Height)) {
            editor.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size)); bmp.Save(path,ImageFormat.Png);
            int left=bmp.Width,top=bmp.Height,right=-1,bottom=-1;
            for(int y=0;y<bmp.Height;y++) for(int x=0;x<bmp.Width;x++) { Color p=bmp.GetPixel(x,y); if(p.R<128 && p.G<128 && p.B<128) { left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y); } }
            if(right<0) return "";
            var mask=new StringBuilder().Append(right-left+1).Append('x').Append(bottom-top+1).Append(':');
            for(int y=top;y<=bottom;y++) for(int x=left;x<=right;x++) { Color p=bmp.GetPixel(x,y); mask.Append(p.R<128 && p.G<128 && p.B<128?'1':'0'); }
            return mask.ToString();
        }
    }
    async Task VerifyInterfaceUpgrade(Action<bool,string> check) {
        string originalQuery=query.Text; Color queryBack=query.BackColor,queryFore=query.ForeColor;
        try {
            query.Text="gypqj"; query.BackColor=Color.White; query.ForeColor=Color.Black;
            using(var reference=new TextBox { Text="gypqj",Font=query.Font,AutoSize=false,BorderStyle=BorderStyle.None,Size=new Size(query.Width,80),Location=new Point(-1000,-1000),BackColor=Color.White,ForeColor=Color.Black }) {
                Controls.Add(reference); reference.CreateControl();
                string actual=GlyphMask(query,Path.Combine(root,"verification","query-glyph.png"));
                string expected=GlyphMask(reference,Path.Combine(root,"verification","query-glyph-reference.png"));
                check(actual.Length>0 && actual==expected,"gypqj 原生文字像素与 80px 无约束编辑框一致，下伸笔画未裁切");
            }
        } finally { query.Text=originalQuery; query.BackColor=queryBack; query.ForeColor=queryFore; }
        var heading=languageLabels.First(pair=>pair.Value=="词语与原句").Key;
        check(tabs.PointToScreen(new Point(0,40)).Y<heading.PointToScreen(Point.Empty).Y,"释义/发音整排页签位于词语与原句标题上方，间隔明确");
        string pendingQuery="gypqj",pendingNote="未保存的笔记 / unsaved note",pendingSentence="Original sentence 原句 draft";
        query.Text=pendingQuery; note.Text=pendingNote; sentence.Text=pendingSentence;
        string savedMovie=Json.Encode(store.LoadMovie(movie.MoviePath)); double before=Json.Num(await mpv.Get("time-pos"));
        languageChoice.OpenDropdown(); TestMouse(PopupSurface(languageChoice),"OnMouseDown",18,60); await Task.Delay(130);
        check(settings.Language=="en" && store.LoadSettings().Language=="en","语言下拉框切换 English，并保存到独立测试设置");
        Property("sub-text","Late subtitle notice / 异步字幕");
        check(lookupButton.Text=="Look up ↵" && tabs.TabPages[0].Text=="Meaning / Audio" && loopSentence.Text=="Loop sentence" && themeChoice.GetItemText(themeChoice.SelectedItem)=="Dark","英文按钮、页签、开关和主题选项同步更新");
        check(query.Text==pendingQuery && note.Text==pendingNote && sentence.Text==pendingSentence,"语言切换保留查询、原句草稿与未保存笔记");
        check(Json.Encode(store.LoadMovie(movie.MoviePath))==savedMovie && Math.Abs(Json.Num(await mpv.Get("time-pos"))-before)<.01,"语言切换不改动电影记录和暂停位置");
        check(!languageLabels.Where(p=>p.Key.Visible).Any(p=>Regex.IsMatch(p.Key.Text,"[\u4e00-\u9fff]")),"已登记的可见界面文案全部切换为英文");
        Status("电影文件不存在：E:\\片源\\gypqj.mkv",true);
        check(status.Text=="Movie file not found: E:\\片源\\gypqj.mkv","动态错误提示翻译界面前缀并保留原始路径");
        Size previous=Size; Size=new Size(1100,800); await Task.Delay(120);
        check(ChildrenFit(primary.Parent) && ChildrenFit(speed.Parent) && ChildrenFit(volume.Parent),"英文界面小窗口播放、音轨及开关行不裁切"); Size=previous; await Task.Delay(100);
        movies.SelectedIndex=-1; movies.EmptyText=Path.GetFileName(movie.MoviePath)+T(" · 外部文件");
        Status("已打开，暂停待跟读 · "+movie.Words.Count+" 条生词记录 · 点击播放 / Space");
        await SavePreview(Path.Combine(root,"verification","interface-english.png"));
        ApplyTheme("light",false); check(query.BackColor.ToArgb()==theme.Field.ToArgb() && EnglishUi,"浅色主题与英文界面可组合使用");
        using(var help=new HelpDialog(theme,true,Icon)) {
            check(help.Content.ReadOnly && help.Content.BackColor.ToArgb()==theme.Field.ToArgb() && help.Content.ForeColor.ToArgb()==theme.Text.ToArgb(),"使用说明为匹配浅色主题的只读文本弹窗");
            check(help.Text.Contains("v"+AppInfo.Version) && help.Content.Text.Contains("Start shadowing") && FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion.StartsWith(AppInfo.Version),"英文说明内容与窗口、EXE 文件版本号一致");
        }
        ApplyTheme("dark",false);
        using(var help=new HelpDialog(theme,false,Icon)) using(var timer=new Timer { Interval=250 }) {
            bool shown=false; timer.Tick+=(s,e)=> { timer.Stop(); shown=help.Visible && help.Content.Text.Contains("开始跟读") && help.Content.BackColor.ToArgb()==theme.Field.ToArgb(); SaveHelpPreview(help,Path.Combine(root,"verification","interface-help.png")); help.CancelButton.PerformClick(); };
            timer.Start(); var result=help.ShowDialog(this);
            check(shown && result==DialogResult.Cancel,"实际打开深色中文说明弹窗并用关闭按钮结束模态窗口");
        }
        languageChoice.OpenDropdown(); TestMouse(PopupSurface(languageChoice),"OnMouseDown",18,23); await Task.Delay(100);
        check(settings.Language=="zh" && store.LoadSettings().Language=="zh" && lookupButton.Text=="查词 ↵","语言切回中文并保存，下次可恢复");
        check(query.Text==pendingQuery && note.Text==pendingNote,"中英往返后未保存输入仍完整保留");
        subText=Json.Str(await mpv.Get("sub-text")); secondText=Json.Str(await GetOptional("secondary-sub-text"));
        await Jump(3); await mpv.WaitForVideo(); await Task.Delay(120);
        check(!sentenceEdited && sentence.Text.Contains("Hello") && Math.Abs(position-7.5)<.2,"主动快进后恢复字幕同步，不保留上一位置的手动原句");
        query.Text=originalQuery; note.Clear(); sentenceEdited=false;
        subText=Json.Str(await mpv.Get("sub-text")); secondText=Json.Str(await GetOptional("secondary-sub-text")); UpdateSentence();
        await Seek(4.5); await mpv.WaitForVideo();
    }
    static void SaveHelpPreview(HelpDialog help,string path) {
        using(var bmp=new Bitmap(help.Width,help.Height)) {
            help.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));
            var p=help.PointToClient(help.Content.PointToScreen(Point.Empty)); p.Offset((help.Width-help.ClientSize.Width)/2,help.Height-help.ClientSize.Height-(help.Width-help.ClientSize.Width)/2);
            using(var g=Graphics.FromImage(bmp)) {
                using(var b=new SolidBrush(help.Content.BackColor)) g.FillRectangle(b,new Rectangle(p,help.Content.Size));
                using(var b=new SolidBrush(help.Content.ForeColor)) g.DrawString(help.Content.Text,help.Content.Font,b,new RectangleF(p.X+3,p.Y+3,help.Content.Width-20,help.Content.Height-6));
            }
            bmp.Save(path,ImageFormat.Png);
        }
    }
}
}
