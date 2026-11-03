using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AppShelf {
 public class InstalledApp {
  public string Name, Version, Publisher, Location, Command, Key, Date, IconPath;
  public bool Machine, Msi;
  public override string ToString() { return Name; }
 }
 public static class Catalog {
  public static List<InstalledApp> Read() {
   var list = new List<InstalledApp>();
   foreach (var hive in new[]{RegistryHive.LocalMachine, RegistryHive.CurrentUser})
    foreach (var view in new[]{RegistryView.Registry64, RegistryView.Registry32}) {
     try {
      using(var root=RegistryKey.OpenBaseKey(hive,view))
      using(var uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")) {
       if(uninstall==null) continue;
       foreach(var key in uninstall.GetSubKeyNames()) {
        try { using(var item=uninstall.OpenSubKey(key)) {
         if(item==null) continue;
         Func<string,string> get=k=>Convert.ToString(item.GetValue(k)??"").Trim();
         string name=get("DisplayName"), command=get("UninstallString");
         if(name.Length==0 || command.Length==0 || get("SystemComponent")=="1" || get("ParentKeyName").Length>0) continue;
         list.Add(new InstalledApp { IconPath=get("DisplayIcon"),Name=name,Version=get("DisplayVersion"),Publisher=get("Publisher"),Location=get("InstallLocation").Trim('"'),Command=command,Machine=hive==RegistryHive.LocalMachine,Msi=get("WindowsInstaller")=="1",Key=hive+"/"+view+"/"+key,Date=get("InstallDate") });
        }} catch(System.Security.SecurityException) {} catch(IOException) {}
       }
      }
     } catch(System.Security.SecurityException) {} catch(UnauthorizedAccessException) {}
    }
   return list.GroupBy(a=>a.Name+"\0"+a.Version+"\0"+a.Command,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  }
  public static ProcessStartInfo Uninstall(InstalledApp app) {
   string raw=Environment.ExpandEnvironmentVariables(app.Command.Trim());
   string exe,args;
   if(raw.StartsWith("\"")) {
    int end=raw.IndexOf('"',1); if(end<0) throw new InvalidOperationException("Неверная команда удаления.");
    exe=raw.Substring(1,end-1); args=raw.Substring(end+1).Trim();
   } else {
    var match=Regex.Match(raw,@"^(.+?\.(?:exe|com))(?=\s|$)",RegexOptions.IgnoreCase);
    if(!match.Success) throw new InvalidOperationException("Команда удаления не содержит поддерживаемый EXE-файл.");
    exe=match.Groups[1].Value; args=raw.Substring(match.Length).Trim();
   }
   string file=Path.GetFileName(exe);
   if(String.Equals(file,"msiexec.exe",StringComparison.OrdinalIgnoreCase)) {
    exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe");
    args=Regex.Replace(args,@"/i(?=\s|\{)","/x",RegexOptions.IgnoreCase);
   } else if(!Path.IsPathRooted(exe) || !File.Exists(exe)) {
    throw new InvalidOperationException("Файл деинсталлятора не найден: "+exe+"\nИспользуйте восстановление приложения или параметры Windows.");
   }
   return new ProcessStartInfo(exe,args) { UseShellExecute=true, Verb=app.Machine?"runas":"", WorkingDirectory=Path.GetDirectoryName(exe) };
  }
  public static List<string> Remnants(InstalledApp app) {
   // Only exact product-name matches. Never infer ownership from a publisher name.
   var found=new List<string>();
   string name=app.Name;
   if(name.Length<4 || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0) return found;
   foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}) {
    if(String.IsNullOrEmpty(root)) continue;
    string candidate=Path.Combine(root,name);
    try { if(Directory.Exists(candidate) && (File.GetAttributes(candidate)&FileAttributes.ReparsePoint)==0) found.Add(candidate); } catch(IOException) {} catch(UnauthorizedAccessException) {}
   }
   return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  }
 }

}

