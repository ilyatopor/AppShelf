using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
namespace AppShelf {
 public static class Tests {
  public static int Run(string[] args){
   var lines=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception("FAIL: "+name);lines.Add("PASS: "+name);};
   string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt");
   try{
    var app=new InstalledApp{Name="Example Player 3.2",Publisher="Example Inc.",Command="MsiExec.exe /I{11111111-1111-1111-1111-111111111111}",Location=@"C:\Program Files\Example Player",Key="test"};
    var msi=Catalog.Uninstall(app);check(msi.Arguments.StartsWith("/x{"),"MSI uninstall command");
    check(Scanner.Names(app).Contains("exampleplayer"),"Version suffix removed");check(Scanner.Names(app).Contains("player"),"Product alias under publisher");
    check(!Scanner.Specific("Microsoft")&&!Scanner.Specific("Common"),"Shared vendor and generic names blocked");
    check(Scanner.Under(@"C:\Apps\Product\bin",@"C:\Apps\Product"),"Path child boundary");check(!Scanner.Under(@"C:\Apps\ProductPlus",@"C:\Apps\Product"),"Sibling with same prefix excluded");
    check(Scanner.ReferencesInstall("\"C:\\Program Files\\Example Player\\player.exe\" /x",app.Location),"Quoted executable path matched");check(!Scanner.ReferencesInstall(@"C:\Other\app.exe --log C:\Program Files\Example Player\log",app.Location),"Argument substring excluded");
    check(!Scanner.SafeFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),"Top-level AppData cannot be deleted");
    var node=new NodeSnapshot{Name="fixture"};node.Values.Add(new ValueSnapshot{Name="",Kind=RegistryValueKind.String,Text="Тест \" кавычки"});node.Values.Add(new ValueSnapshot{Name="expand",Kind=RegistryValueKind.ExpandString,Text="%TEMP%\\data"});node.Values.Add(new ValueSnapshot{Name="multi",Kind=RegistryValueKind.MultiString,Strings=new[]{"one","два",""}});node.Values.Add(new ValueSnapshot{Name="binary",Kind=RegistryValueKind.Binary,Bytes=new byte[]{0,1,255}});node.Values.Add(new ValueSnapshot{Name="dword",Kind=RegistryValueKind.DWord,Number=-1});node.Values.Add(new ValueSnapshot{Name="qword",Kind=RegistryValueKind.QWord,Number=long.MinValue});node.Children.Add(new NodeSnapshot{Name="nested"});
    string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data");Directory.CreateDirectory(dir);string backup=Path.Combine(dir,"roundtrip.xml");Store.Write(backup,node);var roundtrip=Store.Read<NodeSnapshot>(backup);check(Snapshot.Hash(node)==Snapshot.Hash(roundtrip),"XML backup roundtrip all supported value types");check((int)Snapshot.Value(roundtrip.Values[4])==-1,"DWORD signed bit pattern preserved");
    var catalog=Catalog.Read();check(catalog.Count>0,"Catalog readable");check(catalog.All(a=>!String.IsNullOrEmpty(a.Command)),"Catalog commands present");
    var result=Scanner.Scan(new InstalledApp{Name="AppShelfNonexistentTest98761",Publisher="AppShelf",Location="",Key="fixture"});check(result.Items.Count==0,"Unrelated applications produce no candidates");
    bool refused=false;try{CleanupEngine.Clean(new CleanupJob{App=catalog[0],UserSid=Store.Sid});}catch(InvalidOperationException){refused=true;}check(refused,"Installed product cleanup refused before any mutation");
    if(args.Contains("--registry-fixture"))RegistryFixture(check);
    lines.Add("All checks passed. No installed applications were removed.");File.WriteAllLines(output,lines,Encoding.UTF8);return 0;
   }catch(Exception ex){lines.Add(ex.ToString());File.WriteAllLines(output,lines,Encoding.UTF8);return 1;}
  }
  static void RegistryFixture(Action<bool,string> check){
   // All mutations stay inside one freshly generated HKCU test key.
   string name="AppShelfTest"+Guid.NewGuid().ToString("N");string path="SOFTWARE\\"+name;string backupFile=null;
   try{
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.CreateSubKey(path)){
     key.SetValue("", "тест",RegistryValueKind.String);key.SetValue("expand","%TEMP%\\test",RegistryValueKind.ExpandString);key.SetValue("dword",unchecked((int)0xf1234567),RegistryValueKind.DWord);key.SetValue("qword",long.MinValue,RegistryValueKind.QWord);key.SetValue("multi",new[]{"one","два"},RegistryValueKind.MultiString);key.SetValue("binary",new byte[]{0,128,255},RegistryValueKind.Binary);using(var child=key.CreateSubKey("child"))child.SetValue("x","nested");
    }
    var app=new InstalledApp{Name=name,Publisher="AppShelfTests",Key="fixture",Location=""};
    string otherProduct=Catalog.Read().First(a=>Scanner.Specific(a.Name)&&!a.Name.Contains("\\")).Name;
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.CreateSubKey(path+"\\"+otherProduct))key.SetValue("shared","keep");
    check(!Scanner.Scan(app).Items.Any(f=>!f.File&&f.Path==path),"Shared product subtree is never offered for deletion");
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))root.DeleteSubKeyTree(path+"\\"+otherProduct,false);
    var scan=Scanner.Scan(app);var target=scan.Items.First(f=>!f.File&&f.Hive==RegistryHive.CurrentUser&&f.View==RegistryView.Registry64&&f.Path==path);
    check(target.Recommended,"Exact product key recommended");check(scan.Items.Count(f=>!f.File&&f.Path==path)==1,"Shared HKCU key is listed only once across views");
    string hash=target.Stamp;
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.OpenSubKey(path,true))key.SetValue("changed","new");
    bool refused=false;try{CleanupEngine.Clean(new CleanupJob{App=app,UserSid=Store.Sid,Selected=new List<Finding>{target}});}catch(IOException){refused=true;}check(refused,"Changed registry key cannot be cleaned with stale scan");
    target=Scanner.Scan(app).Items.First(f=>!f.File&&f.Hive==RegistryHive.CurrentUser&&f.View==RegistryView.Registry64&&f.Path==path);
    var report=CleanupEngine.Clean(new CleanupJob{App=app,UserSid=Store.Sid,Selected=new List<Finding>{target}});backupFile=report.BackupFile;check(report.Failed.Count==0&&report.Done.Count==1,"Fixture cleanup backed up and completed");
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.OpenSubKey(path))check(key==null,"Fixture key removed");
    check(CleanupEngine.RestoreBackup(backupFile).Count==0,"Fixture backup restored");
    using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))using(var key=root.OpenSubKey(path)){
     check((string)key.GetValue("expand",null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%TEMP%\\test","Expandable data restored literally");check(key.GetValueKind("qword")==RegistryValueKind.QWord&&(long)key.GetValue("qword")==long.MinValue,"QWORD restored");check((int)key.GetValue("dword")==unchecked((int)0xf1234567),"DWORD restored");check(((byte[])key.GetValue("binary")).SequenceEqual(new byte[]{0,128,255}),"Binary restored");using(var child=key.OpenSubKey("child"))check((string)child.GetValue("x")=="nested","Nested key restored");
     var single=Snapshot.Capture(key,"expand");check(single.Values.Count==1&&single.Children.Count==0&&single.Values[0].Name=="expand","Single-value backup never includes sibling data");
    }
    check(CleanupEngine.RestoreBackup(backupFile).Count==1,"Restore never overwrites existing product key");
   }finally{using(var root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64))root.DeleteSubKeyTree(path,false);}
  }
 }
}
