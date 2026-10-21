using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FilmEcho {
public static class Json {
    public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 16000000 };
    public static string Encode(object value) { lock(Serializer) return Serializer.Serialize(value); }
    public static T Decode<T>(string value) { lock(Serializer) return Serializer.Deserialize<T>(value); }
    public static object Parse(string value) { lock(Serializer) return Serializer.DeserializeObject(value); }
    public static Dictionary<string,object> Map(object v) { return v as Dictionary<string,object> ?? new Dictionary<string,object>(); }
    public static string Str(object v) { return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); }
    public static double Num(object v) { double n; return double.TryParse(Str(v),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out n) ? n : 0; }
    public static object Field(object v,string key) { var d=Map(v); return d.ContainsKey(key)?d[key]:null; }
}
public sealed class WordRecord {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Word { get; set; } = "";
    public string Sentence { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string AudioUrl { get; set; } = "";
    public double Seconds { get; set; }
    public double SentenceStart { get; set; } = -1;
    public double SentenceEnd { get; set; } = -1;
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public string Note { get; set; } = "";
    public override string ToString() { return Clock(Seconds)+"   "+Word+ (string.IsNullOrEmpty(Meaning)?"":"   "+Meaning.Split('\n')[0]); }
    public static string Clock(double n) { return TimeSpan.FromSeconds(Math.Max(0,n)).ToString(@"hh\:mm\:ss"); }
}
public sealed class MovieData {
    public string MoviePath { get; set; } = "";
    public double Position { get; set; }
    public string PrimarySubtitle { get; set; } = "auto";
    public string SecondarySubtitle { get; set; } = "no";
    public string AudioTrack { get; set; } = "auto";
    public List<string> ExternalSubtitles { get; set; } = new List<string>();
    public List<WordRecord> Words { get; set; } = new List<WordRecord>();
}
public sealed class AppSettings {
    public string LastMovie { get; set; } = "";
    public string LibraryFolder { get; set; } = AppInfo.PublicRelease?Environment.GetFolderPath(Environment.SpecialFolder.MyVideos):@"E:\英语电影";
    public int JumpSeconds { get; set; } = 3;
    public int Volume { get; set; } = 80;
    public double Speed { get; set; } = 1;
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "zh";
    public bool SkipWelcome { get; set; } = false;
}
public sealed partial class Store {
    public string Root { get; private set; }
    public Store(string root) { Root=root; Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root,"movies")); database=new SqliteDb(DatabaseFile); }
    public static string Hash(string text) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").Substring(0,24); }
    public static string Key(string path) { return Hash(Path.GetFullPath(path).ToUpperInvariant()); }
    public string MovieFile(string path) { return Path.Combine(Root,"movies",Key(path)+".json"); }
    public T Read<T>(string path) where T:new() {
        if(!File.Exists(path)) return new T();
        try { return Json.Decode<T>(File.ReadAllText(path,Encoding.UTF8)); }
        catch(Exception ex) { throw new IOException("记录文件无法读取，已保留原文件；请检查 .bak 备份："+path,ex); }
    }
    public AppSettings LoadSettings() { return Read<AppSettings>(Path.Combine(Root,"settings.json")); }
    public void SaveSettings(AppSettings v) { Write(Path.Combine(Root,"settings.json"),v); }
    public static void Write(string path,object value) {
        string payload=Json.Encode(value);
        if(File.Exists(path) && File.ReadAllText(path,Encoding.UTF8)==payload) return;
        string tmp=path+".tmp";
        using(var stream=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)) {
            var bytes=new UTF8Encoding(false).GetBytes(payload); stream.Write(bytes,0,bytes.Length); stream.Flush(true);
        }
        if(File.Exists(path)) File.Replace(tmp,path,path+".bak"); else File.Move(tmp,path);
    }
    public static string Csv(MovieData movie) {
        Func<string,string> q=s=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        var sb=new StringBuilder("电影,单词,中文或释义,原句,暂停位置,暂停秒数,原句开始秒数,原句结束秒数,查询时间,笔记\r\n");
        foreach(var w in movie.Words) sb.AppendLine(string.Join(",",new[]{q(movie.MoviePath),q(w.Word),q(w.Meaning),q(w.Sentence),q(WordRecord.Clock(w.Seconds)),q(w.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),q(w.SentenceStart.ToString(System.Globalization.CultureInfo.InvariantCulture)),q(w.SentenceEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)),q(w.CreatedAt),q(w.Note)}));
        return sb.ToString();
    }
}
}
