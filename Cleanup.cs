using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace AppShelf {
 public class Finding {
  public bool File, WholeKey, Recommended;
  public RegistryHive Hive;
  public RegistryView View;
  public string Path, ValueName, Reason, Stamp;
  public string Id { get { return File?"file|"+Path.ToLowerInvariant():Hive+"|"+View+"|"+Path.ToLowerInvariant()+"|"+(WholeKey?"KEY":"VALUE:"+ValueName.ToLowerInvariant()); } }
  public string Display { get { return File?Path:(Hive==RegistryHive.CurrentUser?"HKCU":"HKLM")+"\\"+Path+(WholeKey?"":" → "+ValueName)+" ["+(View==RegistryView.Registry64?"64":"32")+" бит]"; } }
 }
 public class ScanResult { public List<Finding> Items=new List<Finding>(); public List<string> Skipped=new List<string>(); }
 public static class Store {
  public static string Home {get{return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AppShelf");}}
  public static string Sid {get{return WindowsIdentity.GetCurrent().User.Value;}}
  public static bool Admin {get{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}}
  public static void Write<T>(string file,T data) {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));using(var s=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.None)){new XmlSerializer(typeof(T)).Serialize(s,data);s.Flush(true);}}
  public static T Read<T>(string file){using(var r=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32000000})){return (T)new XmlSerializer(typeof(T)).Deserialize(r);}}
 }
 public static class Scanner {
  public const string AppPaths=@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
  public const string Run=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
  public const string RunOnce=@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
  static readonly string[] Generic={"software","microsoft","windows","google","intel","amd","nvidia","adobe","apple","iobit","lenovo","common","shared","application","applications","program","programs","program files","data","update","updater","setup","uninstall","cache","temp","system","classes"};
  public static string Normalize(string s){return Regex.Replace((s??"").ToLowerInvariant(),@"[^\p{L}\p{Nd}]","");}
  public static bool Specific(string s){return Normalize(s).Length>=4&&!Generic.Any(g=>Normalize(g)==Normalize(s));}
  public static string ProductName(string s){return Regex.Replace(s??"",@"\s+(?:v(?:ersion)?\s*)?\d+(?:[.\d]+)?(?:\s*\([^)]*\))?\s*$","",RegexOptions.IgnoreCase).Trim();}
  public static HashSet<string> Names(InstalledApp app){
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var n in new[]{app.Name,ProductName(app.Name)})if(Specific(n))names.Add(Normalize(n));
   string publisher=Regex.Replace(app.Publisher??"",@"[,\s]+(?:Inc\.?|LLC|Ltd\.?|Corporation|Corp\.?)$","",RegexOptions.IgnoreCase).Trim();
   string product=ProductName(app.Name);
   if(publisher.Length>=3&&product.StartsWith(publisher+" ",StringComparison.OrdinalIgnoreCase)){
    string shortName=product.Substring(publisher.Length).Trim();if(Specific(shortName))names.Add(Normalize(shortName));
   }
   return names;
  }
  public static bool Under(string child,string parent){try{return System.IO.Path.GetFullPath(child).TrimEnd('\\').StartsWith(System.IO.Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
  public static string InstallRoot(InstalledApp app,List<InstalledApp> current){
   try {
    if(String.IsNullOrWhiteSpace(app.Location)||!System.IO.Path.IsPathRooted(app.Location))return null;
    string full=System.IO.Path.GetFullPath(app.Location).TrimEnd('\\');
    var roots=FileRoots().Concat(new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),System.IO.Path.GetPathRoot(full)});
    if(roots.Any(r=>String.Equals(r.TrimEnd('\\'),full,StringComparison.OrdinalIgnoreCase)))return null;
    if(current.Any(a=>a.Key!=app.Key&&!String.IsNullOrEmpty(a.Location)&&(String.Equals(a.Location.TrimEnd('\\'),full,StringComparison.OrdinalIgnoreCase)||Under(a.Location,full)||Under(full,a.Location))))return null;
    if(!FileRoots().Any(r=>Under(full,r)))return null;
    if(!Specific(System.IO.Path.GetFileName(full)))return null;
    return full;
   }catch{return null;}
  }
  public static string[] FileRoots(){return new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Where(r=>!String.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
  public static bool Installed(InstalledApp app,List<InstalledApp> current){return current.Any(a=>a.Key==app.Key||String.Equals(a.Name,app.Name,StringComparison.OrdinalIgnoreCase));}
  public static ScanResult Scan(InstalledApp app){
   var result=new ScanResult();var current=Catalog.Read();var names=Names(app);string install=InstallRoot(app,current);
   var otherNames=new HashSet<string>(current.Where(a=>a.Key!=app.Key&&!String.Equals(a.Name,app.Name,StringComparison.OrdinalIgnoreCase)).SelectMany(Names));
   names.ExceptWith(otherNames);
   if(names.Count==0){result.Skipped.Add("Недостаточно уникальное название для поиска записей программы.");return result;}
   foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
    // All scanned HKCU branches are shared across WOW64 views; Classes is excluded.
    if(hive==RegistryHive.CurrentUser&&view==RegistryView.Registry32)continue;
    try {using(var root=RegistryKey.OpenBaseKey(hive,view))using(var software=root.OpenSubKey("SOFTWARE")){
     if(software==null)continue;
     foreach(string first in software.GetSubKeyNames()){
      if(new[]{"Microsoft","Classes","WOW6432Node","Policies","Clients","RegisteredApplications"}.Contains(first,StringComparer.OrdinalIgnoreCase))continue;
      string path="SOFTWARE\\"+first;
      bool matched=AddProduct(result,root,hive,view,path,first,names,otherNames);
      if(matched)continue;
      try {using(var vendor=root.OpenSubKey(path)){
       if(vendor==null)continue;
       foreach(string second in vendor.GetSubKeyNames())AddProduct(result,root,hive,view,path+"\\"+second,second,names,otherNames);
      }}catch(UnauthorizedAccessException){result.Skipped.Add(path);}catch(System.Security.SecurityException){result.Skipped.Add(path);}
     }
     if(install!=null){
      using(var paths=view==RegistryView.Registry32?null:root.OpenSubKey(AppPaths))if(paths!=null)foreach(string sub in paths.GetSubKeyNames())using(var item=paths.OpenSubKey(sub)){
       if(item!=null&&ReferencesInstall(Convert.ToString(item.GetValue("","",RegistryValueOptions.DoNotExpandEnvironmentNames)),install))AddRegistry(result,root,new Finding{Hive=hive,View=view,WholeKey=true,Path=AppPaths+"\\"+sub,Recommended=true,Reason="Путь запуска ведёт в папку удалённой программы"});
      }
      foreach(string autorun in new[]{Run,RunOnce})using(var item=root.OpenSubKey(autorun))if(item!=null)foreach(string value in item.GetValueNames()){
       if(ReferencesInstall(Convert.ToString(item.GetValue(value,"",RegistryValueOptions.DoNotExpandEnvironmentNames)),install))AddRegistry(result,root,new Finding{Hive=hive,View=view,Path=autorun,ValueName=value,WholeKey=false,Recommended=true,Reason="Параметр автозапуска ссылается на папку программы"});
      }
     }
    }}catch(Exception ex){if(!(ex is UnauthorizedAccessException||ex is System.Security.SecurityException||ex is IOException))throw;result.Skipped.Add(hive+" / "+view+": "+ex.Message);}
   }
   var fileCandidates=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
   foreach(var root in FileRoots()){
    try {foreach(string path in Directory.GetDirectories(root)){
     string n=Normalize(System.IO.Path.GetFileName(path));if(names.Contains(n))fileCandidates[path]=true;
    }}catch(UnauthorizedAccessException){result.Skipped.Add(root);}catch(IOException){result.Skipped.Add(root);}
    string pub=app.Publisher??"";
    if(pub.Length>0&&pub.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())<0){string vendor=System.IO.Path.Combine(root,pub);try{if(Directory.Exists(vendor))foreach(string path in Directory.GetDirectories(vendor))if(names.Contains(Normalize(System.IO.Path.GetFileName(path))))fileCandidates[path]=true;}catch(UnauthorizedAccessException){}catch(IOException){}}
   }
   if(install!=null&&Directory.Exists(install))fileCandidates[install]=false;
   foreach(var pair in fileCandidates){try{
    string path=System.IO.Path.GetFullPath(pair.Key);
    if(!SafeFolder(path)||current.Any(a=>a.Key!=app.Key&&!String.IsNullOrEmpty(a.Location)&&(Under(a.Location,path)||String.Equals(a.Location.TrimEnd('\\'),path.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))))continue;
    result.Items.Add(new Finding{File=true,Path=path,Recommended=false,Reason=pair.Value?"Название совпадает; папка может содержать личные настройки":"Сохранённая папка установки; проверьте содержимое",Stamp=Directory.GetCreationTimeUtc(path).Ticks.ToString()});
   }catch(IOException){}catch(UnauthorizedAccessException){}}
   result.Items=result.Items.GroupBy(f=>f.Id).Select(g=>g.First()).OrderBy(f=>f.File).ThenByDescending(f=>f.Recommended).ThenBy(f=>f.Display).ToList();return result;
  }
  static bool AddProduct(ScanResult result,RegistryKey root,RegistryHive hive,RegistryView view,string path,string leaf,HashSet<string> names,HashSet<string> otherNames){
   string n=Normalize(leaf);if(!Specific(leaf))return false;
   bool exact=names.Contains(n);bool possible=!exact&&names.Any(x=>x.Length>=6&&n.Contains(x)&&n.Length<=x.Length+24);
   if(!exact&&!possible)return false;
   if(HasOtherProduct(root,path,otherNames,0)){result.Skipped.Add(path+": общий раздел с другой установленной программой");return true;}
   AddRegistry(result,root,new Finding{Hive=hive,View=view,Path=path,WholeKey=true,Recommended=exact,Reason=exact?"Точное название продукта в отдельном разделе":"Частичное совпадение названия; проверьте вручную"});return true;
  }
  static bool HasOtherProduct(RegistryKey root,string path,HashSet<string> names,int depth){
   if(depth>4)return false;try{using(var key=root.OpenSubKey(path)){if(key==null)return true;foreach(var sub in key.GetSubKeyNames()){if(names.Contains(Normalize(sub)))return true;if(HasOtherProduct(root,path+"\\"+sub,names,depth+1))return true;}}return false;}catch{return true;}
  }
  static void AddRegistry(ScanResult result,RegistryKey root,Finding finding){try{using(var key=root.OpenSubKey(finding.Path)){if(key==null)return;var node=Snapshot.Capture(key,finding.WholeKey?null:finding.ValueName);finding.Stamp=Snapshot.Hash(node);result.Items.Add(finding);}}catch(Exception ex){if(ex is OutOfMemoryException)throw;result.Skipped.Add(finding.Display+": "+ex.Message);}}
  public static bool ReferencesInstall(string data,string install){
   if(String.IsNullOrEmpty(data)||String.IsNullOrEmpty(install))return false;
   string expanded=Environment.ExpandEnvironmentVariables(data.Trim());
   string prefix=install.TrimEnd('\\')+"\\";
   // Only the executable at the start of a command, never arbitrary substring matches.
   return expanded.TrimStart('"').StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
  }
  public static bool SafeFolder(string path){
   string full=System.IO.Path.GetFullPath(path).TrimEnd('\\');
   if(!FileRoots().Any(r=>Under(full,r)))return false;
   for(var d=new DirectoryInfo(full);d!=null;d=d.Parent){if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)return false;}
   return true;
  }
 }
 public class ValueSnapshot {public string Name;public RegistryValueKind Kind;public string Text;public string[] Strings;public byte[] Bytes;public long Number;}
 public class NodeSnapshot {public string Name;public List<ValueSnapshot> Values=new List<ValueSnapshot>();public List<NodeSnapshot> Children=new List<NodeSnapshot>();}
 public class BackupEntry {public Finding Target;public NodeSnapshot Data;}
 public class Backup {public string AppName,Created,UserSid;public List<BackupEntry> Entries=new List<BackupEntry>();}
 public class CleanupJob {public InstalledApp App;public string UserSid;public List<Finding> Selected=new List<Finding>();}
 public class CleanupReport {public string BackupFile;public List<string> Done=new List<string>();public List<string> Failed=new List<string>();}
 public static class Snapshot {
  public static NodeSnapshot Capture(RegistryKey key,string value){int remaining=12000;return CaptureNode(key,value,0,ref remaining);}
  static NodeSnapshot CaptureNode(RegistryKey key,string single,int depth,ref int remaining){
   if(depth>48||--remaining<0)throw new IOException("Раздел слишком велик для автоматической очистки.");
   var node=new NodeSnapshot{Name=System.IO.Path.GetFileName(key.Name)};
   foreach(string name in (single==null?key.GetValueNames():new[]{single}).OrderBy(n=>n,StringComparer.Ordinal)){
    if(--remaining<0)throw new IOException("Слишком много параметров.");
    if(!key.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase))throw new IOException("Параметр изменился.");
    var kind=key.GetValueKind(name);object data=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
    var v=new ValueSnapshot{Name=name,Kind=kind};
    switch(kind){case RegistryValueKind.String:case RegistryValueKind.ExpandString:v.Text=(string)data;break;case RegistryValueKind.MultiString:v.Strings=(string[])data;break;case RegistryValueKind.Binary:v.Bytes=(byte[])data;break;case RegistryValueKind.DWord:v.Number=(int)data;break;case RegistryValueKind.QWord:v.Number=(long)data;break;default:throw new IOException("Тип "+kind+" не поддерживает надёжное резервирование; раздел пропущен.");}
    node.Values.Add(v);
   }
   if(single==null)foreach(string sub in key.GetSubKeyNames().OrderBy(n=>n,StringComparer.Ordinal))using(var child=key.OpenSubKey(sub)){if(child==null)throw new IOException("Раздел изменился.");node.Children.Add(CaptureNode(child,null,depth+1,ref remaining));}
   return node;
  }
  public static string Hash(NodeSnapshot node){using(var ms=new MemoryStream()){new XmlSerializer(typeof(NodeSnapshot)).Serialize(ms,node);using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(ms.ToArray()));}}
  public static object Value(ValueSnapshot v){switch(v.Kind){case RegistryValueKind.String:case RegistryValueKind.ExpandString:return v.Text;case RegistryValueKind.MultiString:return v.Strings;case RegistryValueKind.Binary:return v.Bytes;case RegistryValueKind.DWord:return unchecked((int)v.Number);case RegistryValueKind.QWord:return v.Number;default:throw new IOException("Неподдерживаемый тип.");}}
  public static void Restore(RegistryKey key,NodeSnapshot node){foreach(var v in node.Values)key.SetValue(v.Name,Value(v),v.Kind);foreach(var child in node.Children)using(var k=key.CreateSubKey(child.Name))Restore(k,child);}
 }
 public static class CleanupEngine {
  public static CleanupReport Clean(CleanupJob job){
   if(job.UserSid!=Store.Sid)throw new InvalidOperationException("Нельзя очищать профиль другого пользователя.");
   var current=Catalog.Read();if(Scanner.Installed(job.App,current))throw new InvalidOperationException("Программа ещё установлена. Сначала завершите её штатное удаление.");
   var fresh=Scanner.Scan(job.App).Items.ToDictionary(f=>f.Id);
   if(job.Selected.Count==0)throw new InvalidOperationException("Ничего не выбрано.");
   var selected=new List<Finding>();foreach(var f in job.Selected){Finding actual;if(!fresh.TryGetValue(f.Id,out actual)||actual.Stamp!=f.Stamp)throw new IOException("Найденные остатки изменились. Выполните сканирование заново: "+f.Display);selected.Add(actual);}
   var report=new CleanupReport();var backup=new Backup{AppName=job.App.Name,Created=DateTime.Now.ToString("O"),UserSid=Store.Sid};
   foreach(var f in selected.Where(f=>!f.File))using(var root=RegistryKey.OpenBaseKey(f.Hive,f.View))using(var key=root.OpenSubKey(f.Path)){
    if(key==null)throw new IOException("Раздел исчез. Повторите сканирование.");
    var data=Snapshot.Capture(key,f.WholeKey?null:f.ValueName);if(Snapshot.Hash(data)!=f.Stamp)throw new IOException("Раздел изменился: "+f.Display);
    backup.Entries.Add(new BackupEntry{Target=f,Data=data});
   }
   string dir=System.IO.Path.Combine(Store.Home,"Backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
   report.BackupFile=System.IO.Path.Combine(dir,"backup.xml");Store.Write(report.BackupFile,backup);
   // Read back the backup before changing any registry data.
   var saved=Store.Read<Backup>(report.BackupFile);if(saved.Entries.Count!=backup.Entries.Count)throw new IOException("Проверка резервной копии не пройдена.");
   foreach(var f in selected){try{
    if(Scanner.Installed(job.App,Catalog.Read()))throw new IOException("Программа появилась в списке установленных. Очистка остановлена.");
    if(f.File){if(!Directory.Exists(f.Path))continue;if(!Scanner.SafeFolder(f.Path))throw new IOException("Путь папки изменился.");Recycle(f.Path);}
    else using(var root=RegistryKey.OpenBaseKey(f.Hive,f.View)){
     using(var key=root.OpenSubKey(f.Path)){if(key==null)continue;if(Snapshot.Hash(Snapshot.Capture(key,f.WholeKey?null:f.ValueName))!=f.Stamp)throw new IOException("Содержимое изменилось после сканирования.");}
     if(f.WholeKey)root.DeleteSubKeyTree(f.Path,false);else using(var key=root.OpenSubKey(f.Path,true))key.DeleteValue(f.ValueName,false);
    }
    report.Done.Add(f.Display);
   }catch(Exception ex){report.Failed.Add(f.Display+" — "+ex.Message);}}
   Store.Write(System.IO.Path.Combine(dir,"result.xml"),report);return report;
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileOp {public IntPtr hwnd;public uint operation;[MarshalAs(UnmanagedType.LPWStr)]public string from;[MarshalAs(UnmanagedType.LPWStr)]public string to;public ushort flags;[MarshalAs(UnmanagedType.Bool)]public bool aborted;public IntPtr mappings;[MarshalAs(UnmanagedType.LPWStr)]public string title;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHFileOperation(ref FileOp op);
  static void Recycle(string path){var op=new FileOp{operation=3,from=path+"\0\0",flags=0x40|0x4000};int code=SHFileOperation(ref op);if(code!=0||op.aborted)throw new IOException("Перемещение в корзину не завершено ("+code+").");}
  public static List<string> RestoreBackup(string file){
   var backup=Store.Read<Backup>(file);if(backup.UserSid!=Store.Sid)throw new IOException("Резервная копия другого пользователя.");
   var errors=new List<string>();foreach(var entry in backup.Entries){try{
    var f=entry.Target;ValidateRestoreTarget(f);
    using(var root=RegistryKey.OpenBaseKey(f.Hive,f.View)){
     // Do not overwrite settings recreated by an application since cleanup.
     using(var existing=root.OpenSubKey(f.Path))if(existing!=null){if(f.WholeKey)throw new IOException("Раздел уже существует; он оставлен без изменений.");if(existing.GetValueNames().Contains(f.ValueName,StringComparer.OrdinalIgnoreCase))throw new IOException("Параметр уже существует; он оставлен без изменений.");}
     ValidateNode(entry.Data,0);using(var key=root.CreateSubKey(f.Path))Snapshot.Restore(key,entry.Data);
    }
   }catch(Exception ex){errors.Add(entry.Target.Display+": "+ex.Message);}}
   return errors;
  }
  static void ValidateRestoreTarget(Finding f){
   if(f.File||!(f.Hive==RegistryHive.CurrentUser||f.Hive==RegistryHive.LocalMachine)||!(f.View==RegistryView.Registry32||f.View==RegistryView.Registry64))throw new IOException("Неверная цель восстановления.");
   var parts=(f.Path??"").Split('\\');if(parts.Length<2||parts.Any(p=>p.Length==0||p=="."||p=="..")||!parts[0].Equals("SOFTWARE",StringComparison.OrdinalIgnoreCase))throw new IOException("Неверный путь.");
   if(f.WholeKey&&parts.Length<=3&&!new[]{"Microsoft","Classes","Policies","WOW6432Node"}.Contains(parts[1],StringComparer.OrdinalIgnoreCase)&&Scanner.Specific(parts.Last()))return;
   if(f.WholeKey&&f.Path.StartsWith(Scanner.AppPaths+"\\",StringComparison.OrdinalIgnoreCase)&&parts.Length==6)return;
   if(!f.WholeKey&&(f.Path.Equals(Scanner.Run,StringComparison.OrdinalIgnoreCase)||f.Path.Equals(Scanner.RunOnce,StringComparison.OrdinalIgnoreCase))&&f.ValueName!=null)return;
   throw new IOException("Этот системный раздел не поддерживает восстановление из AppShelf.");
  }
  static void ValidateNode(NodeSnapshot node,int depth){if(depth>48)throw new IOException("Слишком глубокая копия.");foreach(var child in node.Children){if(String.IsNullOrEmpty(child.Name)||child.Name.Contains("\\")||child.Name=="."||child.Name=="..")throw new IOException("Неверное имя подраздела.");ValidateNode(child,depth+1);}}
 }
}

