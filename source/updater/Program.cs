using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FilmEcho;

internal static class UpdaterProgram {
    [STAThread] static int Main(string[] args) {
        string root=null,job=null; bool english=false;
        try {
            if(args.Length<2) return 2;
            if(args[0]=="--apply") {
                string planPath=Path.GetFullPath(args[1]); job=Path.GetDirectoryName(planPath);
                var plan=UpdateJson.Read<UpdatePlan>(File.ReadAllText(planPath)); root=Path.GetFullPath(plan.Root); english=plan.English;
                UpdateTransaction.JobRoot(root,job);
                WaitParent(root,plan.ParentPid,plan.ParentStarted,job);
                if(File.Exists(Path.Combine(job,"abort")) || !File.Exists(Path.Combine(job,"apply.ready"))) return 3;
                File.Delete(Path.Combine(job,"apply.ready"));
                if(UpdateSafety.VersionOf(FileVersionInfo.GetVersionInfo(Path.Combine(root,UpdateSafety.ApplicationName)).FileVersion)!=UpdateSafety.VersionOf(plan.CurrentVersion)) throw new UpdateException("version");
                using(var gate=Acquire(root)) { UpdateTransaction.Apply(plan,job); }
                try { Start(root); File.Delete(UpdateTransaction.Marker(root)); }
                catch { var journal=UpdateJson.Read<UpdateJournal>(File.ReadAllText(UpdateTransaction.Marker(root))); journal.State="Applying"; UpdateJson.Write(UpdateTransaction.Marker(root),journal); UpdateTransaction.Rollback(root,job,journal); throw; }
                File.WriteAllText(Path.Combine(job,"result.txt"),"SUCCESS "+DateTime.UtcNow.ToString("o"));
                return 0;
            }
            if(args[0]=="--recover" && args.Length==4) {
                root=Path.GetFullPath(args[1]);
                WaitParent(root,int.Parse(args[2]),long.Parse(args[3]),null);
                using(var gate=Acquire(root)) { UpdateTransaction.Recover(root); }
                Start(root); return 0;
            }
            return 2;
        } catch(Exception ex) {
            try { if(job!=null) File.WriteAllText(Path.Combine(job,"result.txt"),"FAILED "+ex); } catch { }
            string message=english?"The update failed. Your learning data has been kept. Close this message to reopen FilmEcho.":"更新失败，学习数据已保留。关闭此提示后将重新打开 FilmEcho。";
            if(ex is UpdateException && ((UpdateException)ex).Code=="rollback") message=english?"Recovery is incomplete. Keep the .updates folder and restart FilmEcho for recovery.":"旧版本恢复尚未完成，请保留 .updates 文件夹并重新启动 FilmEcho 以恢复。";
            MessageBox.Show(message+"\r\n"+ex.Message,"FilmEcho",MessageBoxButtons.OK,MessageBoxIcon.Error);
            if(root!=null && !File.Exists(UpdateTransaction.Marker(root))) try { Start(root); } catch { }
            return 1;
        }
    }
    sealed class UpdateGate : IDisposable {
        public readonly Mutex Mutex;
        public UpdateGate(string name) { Mutex=new Mutex(false,name); bool acquired=false; try { acquired=Mutex.WaitOne(0); } catch(AbandonedMutexException) { acquired=true; } if(!acquired) { Mutex.Dispose(); throw new UpdateException("locked"); } }
        public void Dispose() { Mutex.ReleaseMutex(); Mutex.Dispose(); }
    }
    static UpdateGate Acquire(string root) {
        using(var sha=SHA256.Create()) {
            string key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-","").Substring(0,24);
            return new UpdateGate("Local\\FilmEcho-"+key);
        }
    }
    static void WaitParent(string root,int pid,long ticks,string job) {
        try {
            using(var parent=Process.GetProcessById(pid)) {
                if(parent.StartTime.ToUniversalTime().Ticks!=ticks) return;
                if(!string.Equals(parent.MainModule.FileName,Path.Combine(root,UpdateSafety.ApplicationName),StringComparison.OrdinalIgnoreCase)) throw new UpdateException("path");
                var until=DateTime.UtcNow.AddMinutes(2);
                while(!parent.WaitForExit(200)) { if(job!=null && File.Exists(Path.Combine(job,"abort"))) return; if(DateTime.UtcNow>=until) throw new UpdateException("locked"); }
            }
        } catch(ArgumentException) { /* The parent already exited. */ }
    }
    static void Start(string root) { using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,UpdateSafety.ApplicationName)) { UseShellExecute=false,WorkingDirectory=root })) { } }
}
