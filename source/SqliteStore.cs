using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FilmEcho {
// Windows' SQLite engine, UTF-8 prepared statements; no SQL built from user text.
sealed class SqliteDb : IDisposable {
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,out IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_text(IntPtr statement,int index,byte[] text,int length,IntPtr destroy);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement,int column);
    IntPtr db;
    static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text+"\0"); }
    static string Text(IntPtr pointer,int length) { if(pointer==IntPtr.Zero || length<=0) return ""; var bytes=new byte[length]; Marshal.Copy(pointer,bytes,0,length); return Encoding.UTF8.GetString(bytes); }
    string Error { get { var p=sqlite3_errmsg(db); int n=0; while(Marshal.ReadByte(p,n)!=0) n++; return Text(p,n); } }
    void Check(int result) { if(result!=0) throw new IOException("SQLite 保存/读取失败："+Error+" ("+result+")"); }
    public SqliteDb(string path) {
        try {
            Check(sqlite3_open_v2(Utf8(path),out db,2|4|0x10000,IntPtr.Zero)); // READWRITE | CREATE | FULLMUTEX
            Check(sqlite3_busy_timeout(db,1000));
            Execute("PRAGMA foreign_keys=ON"); Execute("PRAGMA journal_mode=WAL"); Execute("PRAGMA synchronous=FULL");
            var version=Query("PRAGMA user_version");
            if(version.Count>0 && version[0][0]!="0" && version[0][0]!="1") throw new IOException("学习数据库版本不受支持，已保留原数据库");
            Execute("CREATE TABLE IF NOT EXISTS movies(movie_key TEXT PRIMARY KEY,path TEXT NOT NULL,metadata TEXT NOT NULL)");
            Execute("CREATE TABLE IF NOT EXISTS words(movie_key TEXT NOT NULL,id TEXT NOT NULL,word TEXT NOT NULL,created_at TEXT NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(movie_key,id),FOREIGN KEY(movie_key) REFERENCES movies(movie_key))");
            Execute("CREATE INDEX IF NOT EXISTS words_movie_word ON words(movie_key,word COLLATE NOCASE)");
            Execute("PRAGMA user_version=1");
        } catch { Dispose(); throw; }
    }
    IntPtr Prepare(string sql,string[] values) {
        if(db==IntPtr.Zero) throw new ObjectDisposedException("SQLite");
        IntPtr statement,tail; Check(sqlite3_prepare_v2(db,Utf8(sql),-1,out statement,out tail));
        try { for(int i=0;i<values.Length;i++) { byte[] bytes=Utf8(values[i]??""); Check(sqlite3_bind_text(statement,i+1,bytes,bytes.Length-1,new IntPtr(-1))); } return statement; }
        catch { sqlite3_finalize(statement); throw; }
    }
    public void Execute(string sql,params string[] values) {
        IntPtr statement=Prepare(sql,values);
        try { int result; do { result=sqlite3_step(statement); } while(result==100); if(result!=101) Check(result); }
        finally { sqlite3_finalize(statement); }
    }
    public List<string[]> Query(string sql,params string[] values) {
        IntPtr statement=Prepare(sql,values); var rows=new List<string[]>();
        try { int result; while((result=sqlite3_step(statement))==100) { // The store's SELECTs return exactly one column.
                rows.Add(new[]{Text(sqlite3_column_text(statement,0),sqlite3_column_bytes(statement,0))});
            } if(result!=101) Check(result); return rows;
        } finally { sqlite3_finalize(statement); }
    }
    public void Transaction(Action action) { Execute("BEGIN IMMEDIATE"); try { action(); Execute("COMMIT"); } catch { try { Execute("ROLLBACK"); } catch { } throw; } }
    public void Dispose() { if(db!=IntPtr.Zero) { sqlite3_close_v2(db); db=IntPtr.Zero; } }
}
public sealed partial class Store : IDisposable {
    readonly object databaseGate=new object();
    readonly SqliteDb database;
    public string DatabaseFile { get { return Path.Combine(Root,"learning.sqlite3"); } }
    static void Validate(MovieData movie,string source) {
        if(movie==null || movie.Words==null || movie.ExternalSubtitles==null || movie.Words.Exists(w=>w==null || string.IsNullOrWhiteSpace(w.Id) || w.Word==null || w.Sentence==null || w.Meaning==null || w.Note==null || w.Phonetic==null || w.AudioUrl==null || double.IsNaN(w.Seconds) || double.IsInfinity(w.Seconds) || w.Seconds<0)) throw new IOException("电影记录结构异常，已保留原文件："+source);
        var ids=new HashSet<string>(); foreach(var word in movie.Words) if(!ids.Add(word.Id)) throw new IOException("电影记录有重复 ID，已保留原文件："+source);
    }
    void WriteMetadata(MovieData movie) {
        string key=Key(movie.MoviePath),path=Path.GetFullPath(movie.MoviePath);
        string payload=Json.Encode(new MovieData { MoviePath=path,Position=movie.Position,PrimarySubtitle=movie.PrimarySubtitle,SecondarySubtitle=movie.SecondarySubtitle,AudioTrack=movie.AudioTrack,ExternalSubtitles=movie.ExternalSubtitles });
        database.Execute("INSERT INTO movies(movie_key,path,metadata) VALUES(?,?,?) ON CONFLICT(movie_key) DO UPDATE SET path=excluded.path,metadata=excluded.metadata WHERE metadata<>excluded.metadata",key,path,payload);
    }
    void WriteWord(MovieData movie,WordRecord record) {
        database.Execute("INSERT INTO words(movie_key,id,word,created_at,payload) VALUES(?,?,?,?,?) ON CONFLICT(movie_key,id) DO UPDATE SET word=excluded.word,created_at=excluded.created_at,payload=excluded.payload WHERE payload<>excluded.payload",Key(movie.MoviePath),record.Id,record.Word,record.CreatedAt,Json.Encode(record));
    }
    public MovieData LoadMovie(string path) {
        lock(databaseGate) {
            var rows=database.Query("SELECT metadata FROM movies WHERE movie_key=?",Key(path));
            if(rows.Count==0) {
                // Import once per movie. Original JSON / .bak remain byte-for-byte intact.
                if(!File.Exists(MovieFile(path))) return new MovieData { MoviePath=Path.GetFullPath(path) };
                var legacy=Read<MovieData>(MovieFile(path)); Validate(legacy,MovieFile(path)); legacy.MoviePath=Path.GetFullPath(path); SaveMovie(legacy); return legacy;
            }
            var movie=Json.Decode<MovieData>(rows[0][0]);
            movie.Words=new List<WordRecord>();
            foreach(var row in database.Query("SELECT payload FROM words WHERE movie_key=? ORDER BY rowid",Key(path))) movie.Words.Add(Json.Decode<WordRecord>(row[0]));
            Validate(movie,DatabaseFile); movie.MoviePath=Path.GetFullPath(path); return movie;
        }
    }
    // Bulk merges are for migration/movie boundaries. Queries update one row;
    // existing rows are retained (there is no record-deletion UI).
    public void SaveMovie(MovieData movie) {
        Validate(movie,DatabaseFile);
        lock(databaseGate) database.Transaction(()=> { WriteMetadata(movie); foreach(var word in movie.Words) WriteWord(movie,word); });
    }
    public void SaveRecord(MovieData movie,WordRecord record) {
        lock(databaseGate) database.Transaction(()=> { WriteMetadata(movie); WriteWord(movie,record); });
    }
    public void SaveProgress(MovieData movie) { lock(databaseGate) WriteMetadata(movie); }
    public string IntegrityCheck() { lock(databaseGate) return database.Query("PRAGMA integrity_check")[0][0]; }
    public void Dispose() { lock(databaseGate) database.Dispose(); }
}
}
