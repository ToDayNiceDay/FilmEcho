using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FilmEcho {
static class UiStrings {
#if PUBLIC_RELEASE
    public const string Intro="选择字幕中的单词，或在上方输入。\n\n离线英汉词典：ECDICT，约 66 万词条\n在线英语释义、原声：Free Dictionary\n短语中文翻译：MyMemory\n\n可点击顶部“打开浏览器”，启动系统默认浏览器。\n\n断网仍可查中文释义、英语朗读和查看记录。";
#else
    public const string Intro="选择字幕中的单词，或在上方输入。\n\n离线英汉词典：ECDICT，约 66 万词条\n在线英语释义、原声：Free Dictionary\n短语中文翻译：MyMemory\n\n也可使用旁边的 Google 翻译页面查词和播放发音。\n\n断网仍可查中文释义、英语朗读和查看记录。";
#endif
    public static readonly Dictionary<string,string> English=new Dictionary<string,string> {
        {"英语电影跟读","Movie shadowing"},{"FilmEcho  /  电影跟读","FilmEcho  /  Shadowing"},
        {"跟读学习","Study"},{"使用分布","Usage"},
        {"打开浏览器","Open browser"},{"默认浏览器已打开","Default browser opened"},
        {"打开或拖入电影，开始跟读","Open or drag in a movie to start shadowing"},
        {"已记录新位置 · 使用本片已保存的释义；可以打开默认浏览器自行查询","New position saved · using this movie's saved meaning. You can open your browser."},
        {"查询与原句已保存 · 原声词典 / 英语朗读均可使用","Word and sentence saved · dictionary audio and English speech are available."},
        {"网络查词未返回结果，原句记录已保存；可打开默认浏览器或手动补充笔记","No online result. The sentence was saved; open your browser or add a note."},
        {"本机未安装英语语音；请使用词典原声","No English speech voice is installed. Use dictionary audio."},
        {"英语跟读播放器","FilmEcho"},{"主题","Theme"},{"显示语言","Language"},{"深色 / 黑","Dark"},{"浅色 / 白","Light"},
        {"打开电影","Open movie"},{"电影列表","Movie list"},{"播放选中","Play selected"},{"电影目录","Movie folder"},{"载入字幕","Add subtitles"},{"使用说明","Help"},
        {"正在准备播放器…","Preparing player…"},{@"从 E:\英语电影 选择一部电影，或者拖入视频",@"Select a movie from E:\英语电影, or drag a video here"},
        {"打开电影，开始今天的跟读\n\nSpace 暂停  ·  ← / → 回退和快进  ·  R 重听本句","Open a movie to start shadowing\n\nSpace  play / pause  ·  ← / →  seek  ·  R  replay sentence"},
        {"播放进度","Playback position"},{"▶ 播放","▶ Play"},{"Ⅱ 暂停","Ⅱ Pause"},{"−5秒","−5 s"},{"−3秒","−3 s"},{"+3秒","+3 s"},{"+5秒","+5 s"},
        {"重听本句 R","Replay · R"},{"全屏 F","Full · F"},{"本句循环","Loop sentence"},{"句尾自动暂停","Pause at sentence end"},{"音量","Volume"},{"方向键步长","Seek step"},{"3秒","3 s"},{"5秒","5 s"},
        {"音轨","Audio"},{"字幕","Subtitles"},{"第一字幕","Primary subtitles"},{"第二字幕","Secondary"},
        {"原句 · 暂停后选择单词，或手动补充","Sentence · pause to select a word, or edit below"},{"识别画面字幕 O","OCR captions · O"},
        {"词语与原句","Words & sentences"},{"输入单词或短语","Enter a word or phrase"},{"查词 ↵","Look up ↵"},{"查询会暂停电影，并自动保存单词、原句和时间","Lookup pauses playback and saves the word, sentence and time"},
        {"释义 / 发音","Meaning / Audio"},{"Google 翻译","Google Translate"},{"本片生词","Movie words"},
        {"原声词典发音","Dictionary audio"},{"英语朗读","Read word"},{"朗读原句","Read sentence"},
#if PUBLIC_RELEASE
        {Intro,"Click a word in the captions, or type above.\n\nOffline English–Chinese: ECDICT, about 660,000 entries\nOnline meanings / audio: Free Dictionary\nPhrase translations: MyMemory\n\nUse Open browser at the top to launch your default browser.\n\nOffline meanings, English speech and saved records work without a network connection."},
#else
        {Intro,"Click a word in the captions, or type above.\n\nOffline English–Chinese: ECDICT, about 660,000 entries\nOnline meanings / audio: Free Dictionary\nPhrase translations: MyMemory\n\nGoogle Translate is also available in the next tab.\n\nOffline meanings, English speech and saved records work without a network connection."},
#endif
        {"学习笔记","Study notes"},{"保存笔记","Save note"},{"更新原句","Use sentence"},{"电影位置","Show movie"},{"翻译当前查询","Translate word"},{"翻译原句","Translate sentence"},{"在浏览器打开","Open in browser"},
        {"回到原句","Replay sentence"},{"导出 CSV","Export CSV"},{"记录目录","Records folder"},{"选择电影…","Select a movie…"},{"选择当前电影中的单词。","Select a word from this movie."},
        {" · 外部文件"," · external file"},{"自动（优先英语）","Auto (English first)"},{"自动","Auto"},{"关闭字幕","Off"},{"关闭","Off"},
        {"当前原句 · 点击单词查词 · ","Sentence · click a word · "},{"最近一句（当前无文本字幕） · 可修改原句","Last sentence · no text captions now; you can edit below"},{"无文本字幕 · 图片 / 内嵌字幕可按 O 识别","No text captions · press O to recognize image / burnt-in subtitles"},
        {"画面识别原句 · 请核对 · ","Recognized captions · please check · "},{"\r\n\r\n原句 @ ","\r\n\r\nSentence @ "},{"（查询暂停："," (lookup paused at "},{"）\r\n",")\r\n"},{"\r\n\r\n笔记：","\r\n\r\nNotes: "},
        {"已编辑原句 · 查词使用此内容","Edited sentence · lookup will use this text"},
        {"选择电影目录","Choose the movie folder"},{"选择英语电影","Choose a movie"},{"载入 SRT / ASS / VTT / SUP 字幕","Add SRT / ASS / VTT / SUP subtitles"},{"导出本片生词","Export this movie's words"},
        {"视频|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.ts;*.webm;*.m2ts|全部文件|*.*","Videos|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.ts;*.webm;*.m2ts|All files|*.*"},
        {"字幕|*.srt;*.ass;*.ssa;*.vtt;*.sup;*.sub;*.idx|全部文件|*.*","Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sup;*.sub;*.idx|All files|*.*"},{"CSV 表格|*.csv","CSV files|*.csv"},{"-生词.csv","-words.csv"},
        {"播放器启动失败","Player startup failed"},{"设置读取失败","Cannot read settings"},{"保存失败","Save failed"},
        {"记录未能保存，请检查目录写入权限后再关闭。可从本片生词页导出 CSV。","Your records could not be saved. Check folder write permissions before closing. You can export a CSV from Movie words."},
        {"电影加载超时，请查看 logs 中的播放日志","Movie loading timed out. Check the playback log in logs."},{"无效的发音地址","Invalid pronunciation URL"},{"字幕识别超时，请手动输入原句","Caption recognition timed out. Please type the sentence."},
        {"播放内核尚未准备好","The player is not ready yet."},{"先打开电影，再载入字幕","Open a movie before adding subtitles."},{"电影已到结尾 · 可以回退继续练习","End of movie · seek back to keep practicing."},{"电影解码失败，请查看日志","Movie decoding failed. Please check the log."},
        {"本句循环需要文本字幕。内嵌或图片字幕可用 3/5 秒回退。","Sentence looping needs text subtitles. For image / burnt-in captions, seek back 3 or 5 seconds."},
        {"先打开电影，查询才能关联原句和电影位置","Open a movie so lookups can save its sentence and position."},
        {"已记录新位置 · 使用本片已保存的释义；可在 Google 页继续查词","New position saved · using this movie's saved meaning. Google Translate is also available."},
        {"离线中文释义已保存 · 正在补充在线词典发音","Offline Chinese meaning saved · fetching dictionary audio."},
        {"释义已保存 · 可继续查词，在线发音在后台补充","Meaning saved · ready for another lookup; audio loads in the background."},
        {"原句已保存 · 可继续查词，在线释义在后台查询","Sentence saved · ready for another lookup; online meanings load in the background."},
        {"在线补充未返回发音 · 可继续查词或使用英语朗读","No online audio returned · keep looking up words or use local English speech."},
        {"在线补充已取消或超时 · 离线释义与原句保留，可继续查词","Online request cancelled or timed out · offline meanings and sentences are saved; keep looking up words."},
        {"查询与原句已保存 · 原声词典 / 英语朗读 / Google 翻译均可使用","Word and sentence saved · dictionary audio, English speech and Google Translate are available."},
        {"网络查词未返回结果，原句记录已保存；可用 Google 页面或手动补充笔记","No online result. The sentence was saved; try Google Translate or add a note."},
        {"先查询或选择一条生词记录","Look up a word or select a saved record first."},{"笔记与原句已保存","Note and sentence saved."},
        {"此词没有词典音频，改用本机英语朗读","No dictionary audio; using local English speech."},{"本机未安装英语语音；请使用词典原声或 Google 的发音按钮","No English speech voice is installed. Use dictionary audio or Google Translate."},
        {"正在本机识别画面下方字幕…","Recognizing captions on this computer…"},{"没有识别到字幕；可手动输入原句","No captions recognized. You can type the sentence."},{@"字幕已识别，请核对后点击单词查词；画面保存在 data\ocr",@"Captions recognized. Check the text, then click a word. Frame saved in data\ocr."},
        {"已切换深色主题；下次打开会记住","Dark theme selected; it will be remembered."},{"已切换浅色主题；下次打开会记住","Light theme selected; it will be remembered."}
    };
    static readonly Dictionary<string,string> Formats=new Dictionary<string,string> {
        {"已准备 · 软件解码 / 精确跳转 · Space 暂停，方向键 ±{0} 秒","Ready · software decoding / precise seeking · Space play / pause, arrows ±{0} s"},
        {"启动失败：{0}","Startup failed: {0}"},{"电影目录读取失败：{0}","Cannot read movie folder: {0}"},{"电影文件不存在：{0}","Movie file not found: {0}"},{"打开失败：{0}","Cannot open movie: {0}"},
        {"已打开，暂停待跟读 · {0} 条生词记录 · 点击播放 / Space","Movie opened and paused · {0} saved words · press Play / Space"},
        {"已记录 “{0}” 与 {1} 的原句；正在查词…","Saved “{0}” and the sentence at {1}; looking it up…"},
        {"查询失败：{0}；可在 Google 页继续查词","Lookup failed: {0}; try the Google tab."},{"回到原句：{0} @ {1}","Replaying sentence: {0} @ {1}"},
        {"词典发音失败，改用本机英语朗读：{0}","Dictionary audio failed; using local English speech: {0}"},{"英语朗读不可用：{0}","English speech is unavailable: {0}"},
        {"Google 页面未能连接（{0}）；离线词典与本地记录可继续使用","Google Translate could not connect ({0}). Offline meanings and saved records remain available."},
        {"字幕识别不可用：{0}；可在原句框手动输入","Caption recognition failed: {0}; type the sentence below."},
        {"已导出 {0} 条记录：{1}","Exported {0} records: {1}"},{"保存失败：{0}。请保留播放器窗口，并检查目录写入权限。","Save failed: {0}. Keep the player open and check folder write permissions."},
        {"主题已切换，偏好保存失败：{0}","Theme changed, but the preference could not be saved: {0}"},
        {"查询失败：{0}；可以打开默认浏览器自行查询","Lookup failed: {0}; you can open your default browser."},
        {"默认浏览器无法打开：{0}","Could not open the default browser: {0}"},
        {"语言已切换，偏好保存失败：{0}","Language changed, but the preference could not be saved: {0}"}
        ,{"当前原句 · 点击单词查词 · {0}","Sentence · click a word · {0}"},{"画面识别原句 · 请核对 · {0}","Recognized captions · please check · {0}"}
    };
    sealed class MessageFormat { public Regex Pattern; public string Result; public int Count; }
    static readonly MessageFormat[] Messages=Formats.Select(pair=> {
        int end=0,count=0; var pattern=new StringBuilder("\\A");
        foreach(Match part in Regex.Matches(pair.Key,@"\{(\d+)\}")) { pattern.Append(Regex.Escape(pair.Key.Substring(end,part.Index-end))); int n=int.Parse(part.Groups[1].Value); count=Math.Max(count,n+1); pattern.Append("(?<v"+n+">[\\s\\S]*?)"); end=part.Index+part.Length; }
        pattern.Append(Regex.Escape(pair.Key.Substring(end))).Append("\\z"); return new MessageFormat { Pattern=new Regex(pattern.ToString()),Result=pair.Value,Count=count };
    }).ToArray();
    public static string Text(string source,bool english) {
        if(AppInfo.PublicRelease && source==@"从 E:\英语电影 选择一部电影，或者拖入视频") source="打开或拖入电影，开始跟读";
        if(AppInfo.PublicRelease && source!=null) source=source.Replace("可在 Google 页继续查词","可以打开默认浏览器自行查询").Replace("原声词典 / 英语朗读 / Google 翻译均可使用","原声词典 / 英语朗读均可使用").Replace("可用 Google 页面或手动补充笔记","可打开默认浏览器或手动补充笔记").Replace("请使用词典原声或 Google 的发音按钮","请使用词典原声");
        if(!english || source==null) return source;
        string result; if(English.TryGetValue(source,out result)) return result;
        foreach(var format in Messages) { var match=format.Pattern.Match(source); if(match.Success) return string.Format(format.Result,Enumerable.Range(0,format.Count).Select(n=>(object)match.Groups["v"+n].Value).ToArray()); }
        return source;
    }
}
public sealed partial class MainForm {
    readonly ModernComboBox languageChoice=new ModernComboBox();
    readonly Dictionary<Control,string> languageLabels=new Dictionary<Control,string>();
    readonly Dictionary<Control,string> accessibleLabels=new Dictionary<Control,string>();
    bool changingLanguage; string statusSource="正在准备播放器…",sentenceCaption="原句 · 暂停后选择单词，或手动补充";
    bool EnglishUi { get { return settings.Language=="en"; } }
    string T(string source) { return UiStrings.Text(source,EnglishUi); }
    void CaptureLanguageLabels(Control parent) {
        if((parent is Label || parent is Button || parent is CheckBox || parent is ThemePage) && parent!=status && parent!=movieTitle && parent!=sentenceTitle && parent!=pauseButton && UiStrings.English.ContainsKey(parent.Text)) languageLabels[parent]=parent.Text;
        if(!string.IsNullOrWhiteSpace(parent.AccessibleName) && UiStrings.English.ContainsKey(parent.AccessibleName)) accessibleLabels[parent]=parent.AccessibleName;
        foreach(Control child in parent.Controls) CaptureLanguageLabels(child);
    }
    void ApplyLanguage(string choice,bool save) {
        changingLanguage=true; SuspendLayout();
        try {
            settings.Language=choice=="en"?"en":"zh";
            foreach(var pair in languageLabels) if(!pair.Key.IsDisposed) { var label=pair.Key as Label; if(label!=null) label.UseMnemonic=false; pair.Key.Text=T(pair.Value); }
            foreach(var pair in accessibleLabels) if(!pair.Key.IsDisposed) pair.Key.AccessibleName=T(pair.Value);
            foreach(var combo in AllControls(this).OfType<ModernComboBox>()) if(combo!=movies && combo!=audio && combo!=primary && combo!=secondary && combo!=languageChoice) combo.DisplayText=value=>T(Convert.ToString(value));
            audio.DisplayText=value=>TrackDisplay(value as TrackChoice,true); primary.DisplayText=value=>TrackDisplay(value as TrackChoice,false); secondary.DisplayText=value=>TrackDisplay(value as TrackChoice,false);
            languageChoice.SelectedIndex=EnglishUi?1:0;
            foreach(var combo in AllControls(this).OfType<ModernComboBox>()) combo.RefreshDisplay();
            foreach(var combo in AllControls(this).OfType<ModernComboBox>()) if(combo!=movies) combo.EmptyText=EnglishUi?"Select…":"选择…";
            Text="FilmEcho · "+T("英语电影跟读")+" · v"+AppInfo.Version;
            pauseButton.Text=T(paused?"▶ 播放":"Ⅱ 暂停");
            tabs.TabPages[AppInfo.SavedTabIndex].Text=T("本片生词")+" ("+(movie?.Words.Count??0)+")";
            if(movie==null) movieTitle.Text=T(@"从 E:\英语电影 选择一部电影，或者拖入视频");
            else { movies.EmptyText=Path.GetFileName(movie.MoviePath)+T(" · 外部文件"); movies.Invalidate(); }
            if(currentWord==null) definition.Text=T(movie==null?UiStrings.Intro:"选择当前电影中的单词。");
            else { definition.Text=RecordDetail(currentWord); recordDetail.Text=definition.Text; }
            sentenceTitle.Text=T(sentenceCaption); status.Text=T(statusSource); Invalidate(true);
            usagePage.SetAppearance(theme,EnglishUi);
            RefreshUpdateButton();
        } finally { ResumeLayout(true); changingLanguage=false; }
        if(save) try { store.SaveSettings(settings); } catch(Exception ex) { Status("语言已切换，偏好保存失败："+ex.Message,true); }
    }
    string TrackDisplay(TrackChoice track,bool sound) { if(track==null) return ""; return track.Id=="auto"?T(sound?"自动（优先英语）":"自动"):track.Id=="no"?T("关闭字幕"):track.Name; }
    void SetSentenceCaption(string text) { sentenceCaption=text; sentenceTitle.Text=T(text); }
    static IEnumerable<Control> AllControls(Control parent) { foreach(Control child in parent.Controls) { yield return child; foreach(var nested in AllControls(child)) yield return nested; } }
    string RecordDetail(WordRecord record) { return record.Word+"  "+record.Phonetic+"\r\n"+record.Meaning+T("\r\n\r\n原句 @ ")+WordRecord.Clock(record.SentenceStart>=0?record.SentenceStart:record.Seconds)+T("（查询暂停：")+WordRecord.Clock(record.Seconds)+T("）\r\n")+record.Sentence+T("\r\n\r\n笔记：")+record.Note; }
    void ShowHelp() { using(var help=new HelpDialog(theme,EnglishUi,Icon)) help.ShowDialog(this); }
}
}
