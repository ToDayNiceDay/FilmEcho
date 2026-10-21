using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
namespace FilmEcho {
internal static class UpdateStartup {
    public static bool RecoverIfNeeded(string root) {
        string marker=UpdateTransaction.Marker(root); if(!File.Exists(marker)) return false;
        try {
            var journal=UpdateJson.Read<UpdateJournal>(File.ReadAllText(marker)); string job=UpdateTransaction.JobRoot(root,journal.Job);
            if(journal.State=="Committed") { File.Delete(marker); return false; }
            if(journal.State!="Applying") throw new UpdateException("pending");
            using(var parent=Process.GetCurrentProcess()) {
                var info=new ProcessStartInfo(Path.Combine(job,"FilmEcho.Updater.exe"),"--recover \""+root+"\" "+parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks) { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=job };
                using(var process=Process.Start(info)) { }
            }
        } catch(Exception ex) { MessageBox.Show("上次更新需要恢复，请保留 .updates 文件夹。\r\n"+UpdateMessages.Error(ex,false),"FilmEcho",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        return true;
    }
}
}
