using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using AppShelf;

namespace AppShelfSetup {
 static class Package {
  public const string Version="0.2.0-beta";
  public const string RegistryPath=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppShelf.Setup";
  public static string InstallDir {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","AppShelf");}}
  static string[] Files={"AppShelf.exe","README.md","LICENSE","Uninstall.exe"};
  public static byte[] Resource(string name){using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new IOException("В установщике отсутствует файл: "+name);using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
  public static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
  public static byte[] Payload(){byte[] data=Resource("Payload.AppShelf");string expected=Encoding.ASCII.GetString(Resource("Payload.Hash")).Trim();if(Hash(data)!=expected)throw new IOException("Проверка целостности приложения не пройдена.");if(data.Length<4096||data[0]!=77||data[1]!=90)throw new IOException("Некорректный EXE приложения.");return data;}
  public static void SafePath(string path){string full=Path.GetFullPath(path);if(!Path.IsPathRooted(full))throw new IOException("Требуется абсолютный путь.");for(var d=new DirectoryInfo(full);d!=null;d=d.Parent)if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Установка через ссылки и перенаправленные папки не поддерживается: "+d.FullName);}
  public static void CheckClosed(){foreach(var p in Process.GetProcessesByName("AppShelf")){try{if(String.Equals(p.MainModule.FileName,Path.Combine(InstallDir,"AppShelf.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Закройте установленный AppShelf и повторите операцию.");}catch(System.ComponentModel.Win32Exception){throw new IOException("Не удалось проверить запущенный AppShelf. Закройте его и повторите.");}finally{p.Dispose();}}}
  public static string StartShortcut {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"AppShelf","AppShelf.lnk");}}
  public static string DesktopShortcut {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"AppShelf.lnk");}}
  static object ComGet(object o,string property){return o.GetType().InvokeMember(property,BindingFlags.GetProperty,null,o,null);}
  static void ComSet(object o,string property,object value){o.GetType().InvokeMember(property,BindingFlags.SetProperty,null,o,new[]{value});}
  static object Shell(){var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)throw new IOException("Windows Script Host недоступен для создания ярлыка.");return Activator.CreateInstance(type);}
  static bool OwnedShortcut(string path){if(!File.Exists(path))return false;object shell=null,link=null;try{shell=Shell();link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});return String.Equals(Convert.ToString(ComGet(link,"TargetPath")),Path.Combine(InstallDir,"AppShelf.exe"),StringComparison.OrdinalIgnoreCase);}finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
  static void Shortcut(string path){SafePath(Path.GetDirectoryName(path));if(File.Exists(path)&&!OwnedShortcut(path))throw new IOException("Ярлык с таким названием уже принадлежит другой программе: "+path);Directory.CreateDirectory(Path.GetDirectoryName(path));object shell=null,link=null;try{shell=Shell();link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});ComSet(link,"TargetPath",Path.Combine(InstallDir,"AppShelf.exe"));ComSet(link,"WorkingDirectory",InstallDir);ComSet(link,"IconLocation",Path.Combine(InstallDir,"AppShelf.exe")+",0");ComSet(link,"Description","AppShelf — порядок в приложениях");link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
  public static void WriteFiles(string target,byte[] payload,Action<int,string> progress){
   SafePath(target);Directory.CreateDirectory(target);string stage=Path.Combine(target,".setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var previous=new Dictionary<string,byte[]>();var committed=new List<string>();
   try{
    File.WriteAllBytes(Path.Combine(stage,"AppShelf.exe"),payload);File.WriteAllBytes(Path.Combine(stage,"README.md"),Resource("Payload.Readme"));File.WriteAllBytes(Path.Combine(stage,"LICENSE"),Resource("Payload.License"));File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(stage,"Uninstall.exe"));
    foreach(var name in Files){string destination=Path.Combine(target,name);if(File.Exists(destination)){if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файл является ссылкой: "+destination);previous[name]=File.ReadAllBytes(destination);}else previous[name]=null;}
    int n=0;foreach(var name in Files){string destination=Path.Combine(target,name);File.Copy(Path.Combine(stage,name),destination,true);committed.Add(name);progress(30+(++n)*10,"Копируем файлы приложения…");}
   }catch{foreach(var name in committed.AsEnumerable().Reverse()){string destination=Path.Combine(target,name);if(previous[name]==null)File.Delete(destination);else File.WriteAllBytes(destination,previous[name]);}throw;}
   finally{foreach(string file in Directory.GetFiles(stage))File.Delete(file);Directory.Delete(stage);}
  }
  public static void Install(bool desktop,bool startMenu,Action<int,string> progress){
   SafePath(InstallDir);CheckClosed();progress(8,"Проверяем приложение…");byte[] payload=Payload();
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var existing=root.OpenSubKey(RegistryPath)){
    if(Directory.Exists(InstallDir)&&Files.Any(n=>File.Exists(Path.Combine(InstallDir,n)))&&(existing==null||Convert.ToString(existing.GetValue("AppShelfOwner"))!="AppShelf.Setup.v1"))throw new IOException("Папка AppShelf уже содержит файлы другой установки. Переместите portable-версию или удалите прежнюю установку перед продолжением.");
   }
   // Snapshot the small, explicitly owned installation surface for rollback.
   var oldFiles=new Dictionary<string,byte[]>();foreach(string n in Files){string f=Path.Combine(InstallDir,n);oldFiles[f]=File.Exists(f)?File.ReadAllBytes(f):null;}
   var shortcuts=new[]{startMenu?StartShortcut:null,desktop?DesktopShortcut:null}.Where(x=>x!=null).ToArray();
   foreach(string path in shortcuts){if(File.Exists(path)&&!OwnedShortcut(path))throw new IOException("Ярлык уже используется другой программой: "+path);oldFiles[path]=File.Exists(path)?File.ReadAllBytes(path):null;}
   var registry=new Dictionary<string,object>();var kinds=new Dictionary<string,RegistryValueKind>();bool hadKey;
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var old=root.OpenSubKey(RegistryPath)){hadKey=old!=null;if(old!=null)foreach(var n in old.GetValueNames()){registry[n]=old.GetValue(n,null,RegistryValueOptions.DoNotExpandEnvironmentNames);kinds[n]=old.GetValueKind(n);}}
   try{
    WriteFiles(InstallDir,payload,progress);progress(78,"Создаём ярлыки…");foreach(var shortcut in shortcuts)Shortcut(shortcut);
    progress(90,"Регистрируем приложение в Windows…");using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.CreateSubKey(RegistryPath)){
     key.SetValue("DisplayName","AppShelf");key.SetValue("DisplayVersion",Version);key.SetValue("Publisher","AppShelf contributors");key.SetValue("InstallLocation",InstallDir);key.SetValue("DisplayIcon",Path.Combine(InstallDir,"AppShelf.exe")+",0");key.SetValue("UninstallString","\""+Path.Combine(InstallDir,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);key.SetValue("EstimatedSize",Files.Sum(n=>(int)(new FileInfo(Path.Combine(InstallDir,n)).Length/1024))+1,RegistryValueKind.DWord);key.SetValue("InstallDate",DateTime.Now.ToString("yyyyMMdd"));key.SetValue("AppShelfOwner","AppShelf.Setup.v1");
    }
    progress(100,"AppShelf готов к работе");
   }catch(Exception original){var errors=new List<string>();foreach(var file in oldFiles){try{if(file.Value==null){if(File.Exists(file.Key))File.Delete(file.Key);}else{Directory.CreateDirectory(Path.GetDirectoryName(file.Key));File.WriteAllBytes(file.Key,file.Value);}}catch(Exception ex){errors.Add(ex.Message);}}
    try{using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64)){if(!hadKey)root.DeleteSubKeyTree(RegistryPath,false);else using(var key=root.CreateSubKey(RegistryPath)){foreach(string n in key.GetValueNames())key.DeleteValue(n,false);foreach(var n in registry.Keys)key.SetValue(n,registry[n],kinds[n]);}}}catch(Exception ex){errors.Add(ex.Message);}
    throw new IOException(original.Message+(errors.Count==0?"\nИзменения установки отменены.":"\nНе всё удалось вернуть: "+String.Join("; ",errors)),original);
   }
  }
  public static void Uninstall(Action<int,string> progress){
   SafePath(InstallDir);CheckClosed();using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.OpenSubKey(RegistryPath))if(key==null||Convert.ToString(key.GetValue("AppShelfOwner"))!="AppShelf.Setup.v1")throw new IOException("Установка AppShelf не найдена. Файлы оставлены без изменений.");
   // A locked file is discovered before deleting any files.
   foreach(string n in Files){string f=Path.Combine(InstallDir,n);if(File.Exists(f)){if((File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0)throw new IOException("Обнаружена ссылка: "+f);using(var stream=new FileStream(f,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }}
   int done=0;foreach(string n in Files){string f=Path.Combine(InstallDir,n);if(File.Exists(f))File.Delete(f);progress(15+(++done)*15,"Удаляем файлы AppShelf…");}
   foreach(var link in new[]{StartShortcut,DesktopShortcut}){SafePath(Path.GetDirectoryName(link));if(OwnedShortcut(link))File.Delete(link);}
   string menu=Path.GetDirectoryName(StartShortcut);if(Directory.Exists(menu)&&!Directory.EnumerateFileSystemEntries(menu).Any())Directory.Delete(menu);
   using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))root.DeleteSubKeyTree(RegistryPath,false);
   if(Directory.Exists(InstallDir)&&!Directory.EnumerateFileSystemEntries(InstallDir).Any())Directory.Delete(InstallDir);
   progress(100,"Приложение удалено. Ваши резервные копии сохранены.");
  }
  public static int Test(string directory){Directory.CreateDirectory(directory);var lines=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);lines.Add("PASS: "+name);};string temp=Path.Combine(directory,"loader-fixture-"+Guid.NewGuid().ToString("N"));
   try{byte[] payload=Payload();check(payload.Length>4096,"Embedded application and SHA-256 verified");WriteFiles(temp,payload,(n,s)=>{});check(Files.All(n=>File.Exists(Path.Combine(temp,n))),"All package files extracted");check(Hash(File.ReadAllBytes(Path.Combine(temp,"AppShelf.exe")))==Hash(payload),"Extracted app matches payload");File.WriteAllText(Path.Combine(temp,"user-note.txt"),"keep");WriteFiles(temp,payload,(n,s)=>{});check(File.ReadAllText(Path.Combine(temp,"user-note.txt"))=="keep","Update preserves unrelated files");check(!Directory.GetDirectories(temp,".setup-*").Any(),"No staging folder remains");File.WriteAllText(Path.Combine(temp,"AppShelf.exe"),"old-version");bool rejected=false;try{WriteFiles(temp,payload,(n,s)=>{if(n>=40)throw new IOException("injected-test-failure");});}catch(IOException){rejected=true;}check(rejected&&File.ReadAllText(Path.Combine(temp,"AppShelf.exe"))=="old-version","Interrupted file update restores previous executable");string link=Path.Combine(temp,"fixture.lnk");Shortcut(link);check(OwnedShortcut(link),"Windows shortcut targets the installed application");File.WriteAllLines(Path.Combine(directory,"loader-self-test.txt"),lines,Encoding.UTF8);return 0;}catch(Exception ex){lines.Add("FAIL: "+ex);File.WriteAllLines(Path.Combine(directory,"loader-self-test.txt"),lines,Encoding.UTF8);return 1;}finally{if(Directory.Exists(temp)){foreach(var file in Directory.GetFiles(temp))File.Delete(file);Directory.Delete(temp);}}
  }
 }
 class SetupForm:Form {
  bool uninstall,busy,complete;SoftButton action,cancel;CheckBox desktop,startMenu,launch;Label heading,description,state,path;ProgressBar progress;
  public SetupForm(bool removing){uninstall=removing;Theme.Apply(this);Text=removing?"Удаление AppShelf":"Установка AppShelf";ClientSize=new Size(640,620);MinimumSize=MaximumSize=Size;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(38,30,38,24)};foreach(int h in new[]{92,54,76,68,96,40,40,62})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));Controls.Add(root);
   var logo=new PictureBox{Image=Theme.Logo(),SizeMode=PictureBoxSizeMode.Zoom,Width=76,Height=76,Margin=new Padding(0)};root.Controls.Add(logo);
   heading=Theme.Label(removing?"Удалить AppShelf?":"Знакомьтесь, AppShelf",25,Theme.Text,true);heading.Dock=DockStyle.Fill;root.Controls.Add(heading);
   description=Theme.Label(removing?"Удалим приложение и его ярлыки. Резервные копии реестра и журналы останутся в вашем профиле.":"Порядок в приложениях — в пару кликов.\nУдаление программ, поиск остатков и восстановление реестра.\nВерсия "+Package.Version+" · автономная установка",10,Theme.Muted,false);description.Dock=DockStyle.Fill;root.Controls.Add(description);
   var card=new Card{Dock=DockStyle.Fill,Padding=new Padding(14),Margin=new Padding(0,2,0,6)};path=Theme.Label(Package.InstallDir,9,Theme.Muted,false);path.Dock=DockStyle.Fill;card.Controls.Add(path);root.Controls.Add(card);
   var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,6,0,0)};desktop=new CheckBox{Text="Ярлык на рабочем столе",AutoSize=true,Checked=true,Margin=new Padding(0,0,0,10)};startMenu=new CheckBox{Text="Добавить в меню «Пуск»",AutoSize=true,Checked=true};launch=new CheckBox{Text="Открыть AppShelf после установки",AutoSize=true,Checked=true};options.Controls.Add(desktop);options.Controls.Add(startMenu);launch.Visible=false;options.Controls.Add(launch);root.Controls.Add(options);if(removing){desktop.Visible=false;startMenu.Visible=false;}
   state=Theme.Label(removing?"Для продолжения закройте AppShelf.":"Без интернета и прав администратора.",9,Theme.Muted,false);state.Dock=DockStyle.Fill;root.Controls.Add(state);progress=new ProgressBar{Dock=DockStyle.Top,Height=9,Visible=false,Style=ProgressBarStyle.Continuous,Margin=new Padding(0,9,0,0)};root.Controls.Add(progress,0,6);
   var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};action=new SoftButton(removing?"Удалить":"Установить",true){Width=190};cancel=new SoftButton("Отмена",false){Width=115};buttons.Controls.Add(action);buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,7);action.Click+=(s,e)=>Execute();cancel.Click+=(s,e)=>Close();FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};FormClosed+=(s,e)=>logo.Image.Dispose();
  }
  async void Execute(){if(complete){if(!uninstall&&launch.Checked){try{Process.Start(new ProcessStartInfo(Path.Combine(Package.InstallDir,"AppShelf.exe")){UseShellExecute=true,WorkingDirectory=Package.InstallDir});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Запуск AppShelf");return;}}Close();return;}
   busy=true;action.Enabled=false;cancel.Enabled=false;desktop.Enabled=startMenu.Enabled=false;progress.Visible=true;bool desktopValue=desktop.Checked,startValue=startMenu.Checked;
   Action<int,string> update=(value,text)=>BeginInvoke((Action)(()=>{progress.Value=value;state.Text=text;}));
   try{if(uninstall)await Task.Run(()=>Package.Uninstall(update));else await Task.Run(()=>Package.Install(desktopValue,startValue,update));complete=true;heading.Text=uninstall?"AppShelf удалён":"Всё готово!";description.Text=uninstall?"Резервные копии и журналы сохранены. Спасибо, что попробовали AppShelf.":"AppShelf установлен. Можно навести порядок в приложениях.";desktop.Visible=startMenu.Visible=false;launch.Visible=!uninstall;action.Text=uninstall?"Закрыть":"Готово";cancel.Visible=false;}
   catch(Exception ex){state.Text="Операция не завершена. Можно повторить.";MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Warning);desktop.Enabled=startMenu.Enabled=true;}
   finally{busy=false;action.Enabled=true;cancel.Enabled=true;}
  }
 }
 static class Entry {
  [STAThread]static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{
   if(args.Length==2&&args[0]=="--self-test"){Environment.ExitCode=Package.Test(args[1]);return;}
   if(args.Contains("--uninstall")){
    // Run the remover from a temporary copy, so the installed copy can be deleted.
    string temp=Path.Combine(Path.GetTempPath(),"AppShelf-Remove-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Assembly.GetExecutingAssembly().Location,temp);Process.Start(new ProcessStartInfo(temp,"--uninstall-worker"){UseShellExecute=true});return;
   }
   using(var mutex=new System.Threading.Mutex(false,"Local\\AppShelf.Setup")){if(!mutex.WaitOne(0)){MessageBox.Show("Установка или удаление AppShelf уже открыты.","AppShelf");return;}try{Application.Run(new SetupForm(args.Contains("--uninstall-worker")));}finally{mutex.ReleaseMutex();}}
  }catch(Exception ex){MessageBox.Show(ex.Message,"AppShelf Setup",MessageBoxButtons.OK,MessageBoxIcon.Warning);Environment.ExitCode=1;}}
 }
}


