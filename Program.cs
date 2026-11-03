using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace AppShelf {
 static class Program {
  [STAThread] static void Main(string[] args){
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   try{
    if(args.Contains("--self-test")){Environment.ExitCode=Tests.Run(args);return;}
    if(args.Length==2&&args[0]=="--cleanup"){
     CleanupReport report;try{report=CleanupEngine.Clean(Store.Read<CleanupJob>(args[1]));}catch(Exception ex){report=new CleanupReport();report.Failed.Add(ex.Message);}
     Store.Write(args[1]+".result.xml",report);return;
    }
    if(args.Length==2&&args[0]=="--restore"){var errors=CleanupEngine.RestoreBackup(args[1]);MessageBox.Show(errors.Count==0?"Записи реестра восстановлены.":String.Join("\n",errors),"AppShelf · восстановление");return;}
    if(args.Length==1&&args[0]=="--preview-cleanup"){Application.Run(new CleanupForm(new InstalledApp{Name="AppShelf Preview",Publisher="AppShelf",Location="",Key="preview"}));return;}
    Application.Run(new MainForm());
   }catch(Exception ex){try{Directory.CreateDirectory(Store.Home);File.WriteAllText(Path.Combine(Store.Home,"last-error.txt"),ex.ToString());}catch{}MessageBox.Show(ex.Message,"AppShelf",MessageBoxButtons.OK,MessageBoxIcon.Warning);Environment.ExitCode=1;}
  }
 }
}
