using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FilmEcho {
sealed class HelpDialog : Form {
    readonly ThemePalette palette;
    public readonly RichTextBox Content=new RichTextBox();
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    public HelpDialog(ThemePalette theme,bool english,Icon icon) {
        palette=theme; Text="FilmEcho · "+(english?"Help":"使用说明")+" · v"+AppInfo.Version; Icon=icon;
        Font=new Font("Microsoft YaHei UI",10); BackColor=theme.Background; ForeColor=theme.Text;
        Size=new Size(760,720); MinimumSize=new Size(600,520); StartPosition=FormStartPosition.CenterParent; ShowInTaskbar=false; MinimizeBox=false;
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(22,18,22,16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); Controls.Add(layout);
        layout.Controls.Add(new Label { Text="FilmEcho  /  "+(english?"Help":"使用说明")+"\n"+(english?"Version ":"版本 ")+AppInfo.Version,Font=new Font(Font.FontFamily,15,FontStyle.Bold),ForeColor=theme.Accent,Dock=DockStyle.Fill },0,0);
        Content.ReadOnly=true; Content.BorderStyle=BorderStyle.None; Content.BackColor=theme.Field; Content.ForeColor=theme.Text; Content.Font=Font; Content.Dock=DockStyle.Fill; Content.ScrollBars=RichTextBoxScrollBars.Vertical; Content.DetectUrls=false;
        Content.Text=HelpText(english); layout.Controls.Add(Content,0,1);
        var footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,9,0,0) };
        var close=new ModernButton { Text=english?"Close":"关闭",Size=new Size(102,34),BackColor=theme.Primary,ForeColor=theme.PrimaryText,Palette=theme,DialogResult=DialogResult.Cancel }; footer.Controls.Add(close); layout.Controls.Add(footer,0,2); AcceptButton=close; CancelButton=close;
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); try { int dark=palette.Background.GetBrightness()<.3?1:0; DwmSetWindowAttribute(Handle,20,ref dark,4); } catch { } }
    internal static string HelpText(bool english) {
        string text=english?EnglishHelp:ChineseHelp;
        if(!AppInfo.PublicRelease) return text;
        if(english) {
            text=text.Replace("Google Translate is also available for phrases.","Use Open browser to launch your default browser; no words or sentences are sent automatically.")
                .Replace("Keep the EXE, adjacent DLL files, vendor and source folders together.","Keep the EXE, vendor and runtime folders together.")
                .Replace("The update source is not configured yet.","Updates are checked against the official FilmEcho GitHub releases.");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"^Use Study / Usage.*\r?\nNo national statistics.*\r?\nThe report uses.*$","Use Study / Usage to switch pages. Usage contains only the regional map with provincial boundaries and the South China Sea inset. No national statistics service is connected yet; the map shows no fabricated counts and sends no usage information. Press Esc to return to Study.",System.Text.RegularExpressions.RegexOptions.Multiline);
        } else {
            text=text.Replace("短语也可在 Google 翻译页查询。","可点击顶部“打开浏览器”启动默认浏览器，不会自动发送单词或原句。")
                .Replace("请将 EXE、旁边的 DLL、vendor 和 source 目录放在一起。","请将 EXE、vendor 和 runtime 目录放在一起。")
                .Replace("当前尚未配置更新源，显示“更新服务尚未启用”。","更新信息来自 FilmEcho 官方 GitHub 发布页。");
            text=System.Text.RegularExpressions.Regex.Replace(text,@"^顶部“跟读学习 / 使用分布”.*\r?\n尚未接入全国统计服务.*\r?\n汇总按文件.*$","顶部“跟读学习 / 使用分布”切换主分页。使用分布仅保留地区地图、省界和南海诸岛附图，移除指标选择、查看示例、导入、刷新、概览和排行。尚未接入全国统计服务，地图不显示虚构使用量，也不上传使用信息。在使用分布页按 Esc 返回学习页。",System.Text.RegularExpressions.RegexOptions.Multiline);
        }
        return text;
    }
    static string ChineseHelp { get { return "FilmEcho v"+AppInfo.Version+@"

开始跟读
1. 打开电影，或选择列表中的电影后点“播放选中”。可拖入视频，打开后默认暂停。
2. 确认英语音轨和中英字幕。看一句、暂停、跟读，再按 R 重听原句。
3. 点击原句下方的单词，或输入单词/短语并回车查词。查询会暂停电影，保存词语、原句和位置。
4. 听词典原声，或使用本机英语朗读；短语也可在 Google 翻译页查询。
5. 在“本片生词”双击记录或点“回到原句”，从台词开始处重听。
离线释义显示后即可继续查词，不必等待在线发音。新查询会取消上一词未完成的在线补充；离线释义和原句仍保留。在线请求在后台进行，最长等待约 6 秒，不锁定查询按钮。

播放快捷键
Space：播放 / 暂停
← / →：按所选步长回退 / 快进 3 秒或 5 秒
Ctrl+← / →：固定 5 秒；Shift+← / →：固定 3 秒
R：重听最近一句；无文本字幕时回退所选步长
O：识别当前画面下方的字幕；F：全屏 / 退出全屏
Ctrl+F：聚焦查词框；Ctrl+O：打开电影
Esc：取消展开的下拉框；再次按 Esc 返回播放按钮
Ctrl+Tab / Ctrl+Shift+Tab：切换右侧页签

界面设置
右上角选择深色 / 浅色与中文 / English。立即生效，下次打开自动恢复。
右上角“更新”可检查新版。发现更新时按钮亮起并显示黄色圆点；查看说明并下载后，选择“重启并更新”。升级前保存当前笔记和播放进度，保留 data 中的学习记录。当前尚未配置更新源，显示“更新服务尚未启用”。
显示语言只改变界面文案，保留电影字幕、英汉释义与笔记原文。
顶部“跟读学习 / 使用分布”切换主分页；使用分布采用真实省级地理边界地图、南海诸岛附图、概览卡片与排行，可切换指标、点击地区或使用方向键查看详情。地图内嵌在程序中，可离线显示。
尚未接入全国统计服务。点“查看示例”只预览布局，所有数字明确标为示例，不写入真实汇总。可导入约定格式的 JSON 汇总，刷新重新读取 data\usage-summary.json；当前页面不会上传使用信息。
汇总按文件中的统计日期展示，安装数不等于真实人数。小窗口可滚动查看；在使用分布页按 Esc 返回学习页。
下拉框用 Enter、Space、F4 或 Alt+↓ 展开；方向键移动，Enter 确认，Esc 取消，Tab 继续导航。也可用滚轮翻页。
输入框和网页有焦点时，键盘用于输入；下拉框和滑条有焦点时，方向键操作控件。
单击视频画面会交回播放焦点并切换播放 / 暂停，随后可直接使用 Space、方向键与 R；查词草稿与未保存笔记保留。双击视频切换全屏并交回播放焦点。

字幕与记录
文字字幕可直接点击单词。内嵌或 PGS/SUP 图片字幕需按 O 识别，再核对文字；可手动载入匹配的 SRT / ASS 字幕。
本句循环和句尾自动暂停需要有效的文本字幕起止时间。跟读正确性由自己听原音判断，程序不自动评分。
学习记录保存在 data\learning.sqlite3，按电影完整路径隔离；查询和笔记按条写入，播放进度单独保存。旧 data\movies 的 JSON 首次打开对应电影时自动迁移，原文件和 .bak 保留，但以后以数据库为准。界面偏好仍在 data\settings.json。可以导出本片生词 CSV。关闭播放器后备份整个 data 目录。
请将 EXE、旁边的 DLL、vendor 和 source 目录放在一起。
"; } }
    static string EnglishHelp { get { return "FilmEcho v"+AppInfo.Version+@"

Start shadowing
1. Open a movie, choose one from the list and press Play selected, or drag a video here. Movies open paused.
2. Select the English audio and bilingual subtitles. Watch a sentence, pause, repeat it, then press R to hear it again.
3. Click a word below the sentence, or type a word / phrase and press Enter. Lookup pauses the movie and saves the word, sentence and position.
4. Play dictionary audio or local English speech. Google Translate is also available for phrases.
5. Double-click a saved word, or press Replay sentence, to hear the original line from its beginning.
Keep looking up words as soon as offline meanings appear; online audio loads in the background. A new lookup cancels the previous unfinished online request, preserving its offline meaning and sentence. Online requests have a six-second limit and never lock the lookup button.

Playback shortcuts
Space: play / pause
← / →: seek back / forward by the selected 3 or 5 seconds
Ctrl+← / →: 5 seconds; Shift+← / →: 3 seconds
R: replay the last sentence; without text captions, seek back by the selected step
O: recognize captions at the bottom of the frame; F: fullscreen / exit fullscreen
Ctrl+F: focus lookup; Ctrl+O: open a movie
Esc: cancel the open dropdown; press Esc again to return to the playback button
Ctrl+Tab / Ctrl+Shift+Tab: switch study tabs

Interface settings
Choose Dark / Light and 中文 / English at the top right. Preferences take effect immediately and are remembered.
Use Update at the top right to check for a new version. An available update lights up the button with a yellow dot. Read the notes, download, then choose Restart & update. Your current notes and progress are saved and learning records in data are kept. The update source is not configured yet.
Display language changes interface text. Movie subtitles, English–Chinese meanings and your notes keep their original content.
Use Study / Usage to switch main pages. Usage shows real provincial boundaries, a South China Sea inset, overview cards and a ranking; switch metrics and click regions or navigate with arrow keys. The embedded map works offline.
No national statistics service is connected yet. Preview layout uses clearly marked demo data and does not save a real summary. Import a JSON summary in the supported format; Refresh reads data\usage-summary.json again. This page sends no usage information.
The report uses its own date window; installation counts are not people. Scroll in small windows; press Esc on Usage to return to Study.
Open dropdowns with Enter, Space, F4 or Alt+↓. Use arrows to move, Enter to confirm, Esc to cancel and Tab to continue. Scroll the mouse wheel to browse long lists.
Typing areas and web pages use keys for editing. When a dropdown or slider has focus, arrows operate that control.
Click the video to return keyboard focus to playback and toggle play / pause; Space, arrows and R work immediately. Lookup drafts and unsaved notes are preserved. Double-click toggles fullscreen and returns playback focus.

Subtitles and saved records
Text captions support clickable words. For burnt-in or PGS/SUP image captions, press O and check the recognized text. You can also add matching SRT / ASS files.
Sentence loops and automatic end pauses need valid text-caption timing. Compare your speech with the original audio yourself; this app does not score pronunciation.
Study records: data\learning.sqlite3, separated by each movie's full path. Words and notes update individual rows; playback progress saves separately. Legacy JSON files in data\movies migrate when their movie first opens. Original files and .bak backups are kept; subsequent changes use the database. Preferences remain in data\settings.json. Export this movie's words as CSV. Close the player before backing up the entire data folder.
Keep the EXE, adjacent DLL files, vendor and source folders together.
"; } }
}
}
