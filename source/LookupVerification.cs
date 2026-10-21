using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FilmEcho {
sealed class LookupFixtureHandler : HttpMessageHandler {
    internal static LookupFixtureHandler Current;
    internal readonly Dictionary<string,TaskCompletionSource<bool>> Held=new Dictionary<string,TaskCompletionSource<bool>>();
    internal readonly HashSet<string> IgnoreCancellation=new HashSet<string>();
    internal int Cancelled;
    internal LookupFixtureHandler() { Current=this; }
    internal void Hold(string word,bool ignore=false) { Held[word]=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); if(ignore) IgnoreCancellation.Add(word); }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {
        string word=request.RequestUri.Host=="api.dictionaryapi.dev"?Uri.UnescapeDataString(request.RequestUri.Segments.Last()):"phrase";
        TaskCompletionSource<bool> held;
        if(Held.TryGetValue(word,out held)) {
            if(IgnoreCancellation.Contains(word)) await held.Task;
            else { await Task.WhenAny(held.Task,Task.Delay(Timeout.Infinite,cancellationToken)); if(cancellationToken.IsCancellationRequested) { Interlocked.Increment(ref Cancelled); cancellationToken.ThrowIfCancellationRequested(); } }
        }
        var entry=new {
            phonetic="/test/",
            phonetics=new[]{new {text="/test/",audio="https://fixture.invalid/"+word+".mp3"}},
            meanings=new[]{new {partOfSpeech="noun",definitions=new[]{new {definition="Fixture meaning for "+word,example="A fixture sentence."}}}}
        };
        string payload=word=="phrase"?"{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"模拟短语翻译\"}}":Json.Encode(new[]{entry});
        return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(payload,Encoding.UTF8,"application/json") };
    }
}
public sealed partial class MainForm {
    static async Task<bool> LookupUntil(Func<bool> condition,int milliseconds=2000) { var watch=Stopwatch.StartNew(); while(!condition() && watch.ElapsedMilliseconds<milliseconds) await Task.Delay(20); return condition(); }
    async Task VerifyLookup() {
        var report=new StringBuilder(); int failed=0;
        Action<bool,string> check=(ok,name)=> { report.AppendLine((ok?"PASS: ":"FAIL: ")+name); if(!ok) failed++; File.WriteAllText(Path.Combine(root,"verification","lookup-results.txt"),report.ToString(),Encoding.UTF8); };
        Action<string> metric=value=>report.AppendLine("METRIC: "+value);
        try {
            var handler=LookupFixtureHandler.Current;
            await Seek(4.5); await mpv.WaitForVideo(); await offline.Find("hello");
            handler.Hold("hello"); query.Text="hello";
            var watch=Stopwatch.StartNew(); await Lookup(); watch.Stop(); Task oldSupplement=lookupSupplement;
            metric("Warm offline lookup foreground="+watch.ElapsedMilliseconds+" ms");
            check(watch.ElapsedMilliseconds<1500 && lookupButton.Enabled && !oldSupplement.IsCompleted && currentWord.Meaning.Contains("[离线词典"),"在线请求尚未完成时，离线释义已显示并可立即再次查询");
            WordRecord hello=currentWord; query.Text="world"; lookupButton.PerformClick();
            check(await LookupUntil(()=>currentWord!=null && currentWord.Word=="world" && currentWord.AudioUrl.Length>0),"通过实际查询按钮发起第二个词，慢请求不阻止第二次查询完成");
            await oldSupplement;
            check(handler.Cancelled>0 && currentWord.Word=="world" && query.Text=="world","旧在线请求被取消，旧结果不覆盖新词或输入内容");
            var saved=store.LoadMovie(movie.MoviePath);
            check(saved.Words.Any(w=>w.Id==hello.Id && w.Meaning.Length>0 && w.Sentence.Contains("Hello")) && saved.Words.Any(w=>w.Word=="world" && w.AudioUrl.Length>0),"连续查询的两个词与对应原句均保存在当前电影下");
            int count=movie.Words.Count; await Lookup(); await lookupSupplement;
            check(movie.Words.Count==count,"同一词、同一位置重复查询复用记录，不重复新增");
            handler.Hold("book"); query.Text="book"; await Lookup(); Task bookSupplement=lookupSupplement;
            note.Text="正在编辑，不能被在线回调清空"; handler.Held["book"].TrySetResult(true); await bookSupplement;
            check(note.Text=="正在编辑，不能被在线回调清空" && currentWord.AudioUrl.Contains("book"),"在线补充发音时更新释义区域，保留正在输入的笔记草稿");
            SaveNote(); check(store.LoadMovie(movie.MoviePath).Words.Any(w=>w.Word=="book" && w.Note==note.Text),"笔记只更新所选电影中的对应记录并可重载");
            history.SelectedItem=hello; query.Text="chair"; await Lookup(); await lookupSupplement;
            check(query.Text=="chair" && currentWord.Word=="chair" && history.SelectedItem==currentWord,"选中过历史记录后再查新词，增量列表事件不把输入改回旧词");
            handler.Hold("happy",true); query.Text="happy"; await Lookup(); Task happySupplement=lookupSupplement;
            MovieData previous=movie; string alternate=Path.Combine(store.Root,"alternate.mkv"); File.Copy(Path.Combine(root,"verification","sample.mkv"),alternate);
            await LoadMovie(alternate); handler.Held["happy"].TrySetResult(true); await happySupplement;
            check(movie.MoviePath==alternate && movie.Words.Count==0 && currentWord==null && store.LoadMovie(previous.MoviePath).Words.Any(w=>w.Word=="happy" && w.AudioUrl.Length==0),"切换电影后，即使旧网络响应晚到，也不污染新电影或覆盖当前页面");
            handler.Hold("phrase"); query.Text="a fixture phrase outside dictionary"; watch.Restart(); await Lookup(); watch.Stop(); Task timeoutSupplement=lookupSupplement;
            check(lookupButton.Enabled && watch.ElapsedMilliseconds<1500 && !timeoutSupplement.IsCompleted,"未命中离线词库的短语也立即保存原句，不锁查询按钮");
            watch.Restart(); await timeoutSupplement; watch.Stop(); metric("Simulated online timeout="+watch.ElapsedMilliseconds+" ms");
            check(watch.ElapsedMilliseconds>=5500 && watch.ElapsedMilliseconds<8500 && lookupButton.Enabled && store.LoadMovie(movie.MoviePath).Words.Any(w=>w.Word.Contains("fixture phrase")),"后台六秒超时后原句记录保留，查询按钮始终可用");
            string scaleRoot=Path.Combine(root,"verification","sqlite-scale-"+Guid.NewGuid().ToString("N"));
            using(var scale=new Store(scaleRoot)) {
                var large=new MovieData { MoviePath=Path.Combine(scaleRoot,"电影甲","same.mkv"),Position=4.5 };
                for(int i=0;i<5000;i++) large.Words.Add(new WordRecord { Word="word"+i,Sentence="Original sentence 原句 "+i,Meaning="释义 "+i,Seconds=i,CreatedAt="2000-01-01 00:00:00" });
                string baseline=Path.Combine(scaleRoot,"old-json.json"); watch.Restart(); Store.Write(baseline,large); watch.Stop(); metric("Old full JSON first write, 5000 records="+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms");
                watch.Restart(); scale.SaveMovie(large); watch.Stop(); metric("SQLite bulk insert, 5000 records="+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms");
                var timings=new List<double>(); var oldTimings=new List<double>();
                for(int i=0;i<30;i++) { large.Words[2500].Note="incremental "+i; watch.Restart(); scale.SaveRecord(large,large.Words[2500]); watch.Stop(); timings.Add(watch.Elapsed.TotalMilliseconds); }
                for(int i=0;i<5;i++) { large.Words[2500].Note="JSON update "+i; watch.Restart(); Store.Write(baseline,large); watch.Stop(); oldTimings.Add(watch.Elapsed.TotalMilliseconds); }
                timings.Sort(); oldTimings.Sort(); metric("SQLite single-record save with 5000 existing records: median="+timings[15].ToString("F2")+" ms, p95="+timings[28].ToString("F2")+" ms; old full JSON median="+oldTimings[2].ToString("F2")+" ms");
                check(timings[28]<250 && scale.LoadMovie(large.MoviePath).Words.Count==5000,"5000 条记录时按条保存，p95 小于 250ms，重载数量正确");
                WordRecord malicious=large.Words[0]; malicious.Word="g'); DROP TABLE words; --"; malicious.Note="中文 \"引号\" 与换行\r\nSQL 参数测试"; scale.SaveRecord(large,malicious);
                check(scale.LoadMovie(large.MoviePath).Words[0].Note==malicious.Note && scale.IntegrityCheck()=="ok","参数绑定正确保留中文、引号、换行和 SQL 特殊字符，数据库完整");
                string untouched=Json.Encode(scale.LoadMovie(large.MoviePath).Words); large.Position=123.45; scale.SaveProgress(large);
                var reloaded=scale.LoadMovie(large.MoviePath); check(reloaded.Position==123.45 && Json.Encode(reloaded.Words)==untouched,"定时保存只更新电影进度，不改写或丢失 5000 条生词");
                var peer=new MovieData { MoviePath=Path.Combine(scaleRoot,"电影乙","same.mkv") };
                var shared=new WordRecord { Id=malicious.Id,Word=malicious.Word,Sentence="属于电影乙的句子",Note="乙电影笔记" }; peer.Words.Add(shared); scale.SaveRecord(peer,shared);
                check(scale.LoadMovie(peer.MoviePath).Words.Single().Note=="乙电影笔记" && scale.LoadMovie(large.MoviePath).Words[0].Note==malicious.Note,"同名电影、相同单词甚至相同记录 ID 按电影键隔离，不串记录");
                using(var inspect=new SqliteDb(scale.DatabaseFile)) check(inspect.Query("PRAGMA journal_mode")[0][0]=="wal" && inspect.Query("SELECT count(*) FROM words")[0][0]=="5001","数据库实际使用 WAL，两个电影共 5001 条独立记录");
            }
            string legacyRoot=Path.Combine(root,"verification","sqlite-migrate-"+Guid.NewGuid().ToString("N"));
            using(var migration=new Store(legacyRoot)) {
                var legacy=new MovieData { MoviePath=Path.Combine(legacyRoot,"legacy.mkv"),Position=123.125,PrimarySubtitle="2",SecondarySubtitle="4",AudioTrack="3",ExternalSubtitles=new List<string>{"字幕.srt"},Words=new List<WordRecord>{new WordRecord {Word="hello",Sentence="原句 hello",Meaning="你好",Note="旧笔记",Seconds=40.5,SentenceStart=39,SentenceEnd=42}} };
                Store.Write(migration.MovieFile(legacy.MoviePath),legacy); byte[] before=File.ReadAllBytes(migration.MovieFile(legacy.MoviePath));
                check(Json.Encode(migration.LoadMovie(legacy.MoviePath))==Json.Encode(legacy) && File.ReadAllBytes(migration.MovieFile(legacy.MoviePath)).SequenceEqual(before),"旧 JSON 自动迁移，生词、原句、笔记、字幕、音轨与进度完整，原文件未改动");
                Store.Write(migration.MovieFile(legacy.MoviePath),new MovieData { MoviePath=legacy.MoviePath });
                check(migration.LoadMovie(legacy.MoviePath).Words.Count==1,"已迁移电影以数据库为准，旧 JSON 后续变化不重复导入");
                string brokenPath=Path.Combine(legacyRoot,"broken.mkv"); File.WriteAllText(migration.MovieFile(brokenPath),"{broken"); bool refused=false;
                try { migration.LoadMovie(brokenPath); } catch(IOException) { refused=true; }
                check(refused && File.ReadAllText(migration.MovieFile(brokenPath))=="{broken","损坏旧记录拒绝迁移并保留原文件");
                string actualDir=Path.Combine(root,"data","movies"); int migrated=0;
                foreach(string file in Directory.GetFiles(actualDir,"*.json")) {
                    var actual=Json.Decode<MovieData>(File.ReadAllText(file,Encoding.UTF8)); string destination=migration.MovieFile(actual.MoviePath); File.Copy(file,destination,true);
                    var loaded=migration.LoadMovie(actual.MoviePath); check(Json.Encode(loaded)==Json.Encode(actual) && File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(destination)),"现有电影记录副本完整迁移："+Path.GetFileName(file)); migrated++;
                }
                metric("Existing personal movie files checked by copies="+migrated);
                string rollbackMovie=Path.Combine(legacyRoot,"rollback.mkv");
                using(var fixture=new SqliteDb(migration.DatabaseFile)) fixture.Execute("CREATE TRIGGER refuse_fixture BEFORE INSERT ON words WHEN NEW.word='trigger-failure' BEGIN SELECT RAISE(ABORT,'fixture rollback'); END");
                bool rolledBack=false; try { migration.SaveRecord(new MovieData {MoviePath=rollbackMovie},new WordRecord {Word="trigger-failure"}); } catch(IOException) { using(var inspect=new SqliteDb(migration.DatabaseFile)) rolledBack=inspect.Query("SELECT count(*) FROM movies WHERE movie_key=?",Store.Key(rollbackMovie))[0][0]=="0"; }
                check(rolledBack && migration.IntegrityCheck()=="ok","模拟写入失败时整笔事务回滚，不留下半条电影/生词记录");
            }
            movie.Words.Clear(); for(int i=0;i<5000;i++) movie.Words.Add(new WordRecord {Word="ui-word"+i,Meaning="缓存释义",Seconds=4.5,CreatedAt="2000-01-01 00:00:00"}); store.SaveMovie(movie); RefreshHistory();
            query.Text="apple"; watch.Restart(); await Lookup(); watch.Stop(); await lookupSupplement;
            metric("Warm foreground lookup with 5000 UI records="+watch.ElapsedMilliseconds+" ms");
            check(watch.ElapsedMilliseconds<1500 && history.Items.Count==5001 && store.LoadMovie(movie.MoviePath).Words.Count==5002,"5000 条界面记录时查询仍快速返回，增量更新列表和独立数据库记录");
        } catch(Exception ex) { check(false,"查询/数据库验证异常："+ex); }
        finally { report.AppendLine("FAIL="+failed); File.WriteAllText(Path.Combine(root,"verification","lookup-results.txt"),report.ToString(),Encoding.UTF8); Environment.ExitCode=failed==0?0:1; Close(); }
    }
}
}
