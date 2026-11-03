using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
namespace AppShelf {
 public class BackupsForm:Form {
  DataGridView grid;Label status;SoftButton restore,open;bool busy;
  public BackupsForm(){Theme.Apply(this);Text="AppShelf · восстановление";Size=new Size(920,620);MinimumSize=new Size(800,520);StartPosition=FormStartPosition.CenterParent;
   var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=5};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));Controls.Add(layout);
   var heading=Theme.Label("Всегда можно вернуться",23,Theme.Text,true);heading.Dock=DockStyle.Fill;layout.Controls.Add(heading);var note=Theme.Label("Выберите копию реестра и нажмите «Восстановить». Уже существующие записи сохранятся.\nУдалённые папки возвращаются отдельно из корзины Windows.",10,Theme.Muted,false);note.Dock=DockStyle.Fill;layout.Controls.Add(note);
   grid=new DataGridView();Theme.Grid(grid);grid.RowTemplate.Height=52;grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Product",HeaderText="ПРИЛОЖЕНИЕ",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=50});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Date",HeaderText="СОЗДАНА",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=35});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Count",HeaderText="ЗАПИСЕЙ",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=15});grid.SelectionChanged+=(s,e)=>{restore.Enabled=!busy&&grid.SelectedRows.Count>0;open.Enabled=grid.SelectedRows.Count>0;};layout.Controls.Add(grid);
   status=Theme.Label("",9,Theme.Muted,false);status.Dock=DockStyle.Fill;layout.Controls.Add(status);var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};restore=new SoftButton("Восстановить",true){Width=180};open=new SoftButton("Открыть папку",false){Width=155};var close=new SoftButton("Закрыть",false){Width=120};buttons.Controls.Add(restore);buttons.Controls.Add(open);buttons.Controls.Add(close);layout.Controls.Add(buttons);close.Click+=(s,e)=>Close();open.Click+=(s,e)=>{if(grid.SelectedRows.Count>0)Process.Start(new ProcessStartInfo("explorer.exe","\""+Path.GetDirectoryName((string)grid.SelectedRows[0].Tag)+"\""){UseShellExecute=true});};restore.Click+=(s,e)=>RestoreAsync();Shown+=(s,e)=>LoadBackups();FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
  }
  void LoadBackups(){try{string root=Path.Combine(Store.Home,"Backups");Directory.CreateDirectory(root);int skipped=0;foreach(string file in Directory.GetFiles(root,"backup.xml",SearchOption.AllDirectories).OrderByDescending(f=>f).Take(1000)){
    try{var backup=Store.Read<Backup>(file);if(backup.UserSid!=Store.Sid)continue;DateTime date;string created=DateTime.TryParse(backup.Created,out date)?date.ToString("dd.MM.yyyy  HH:mm"):backup.Created;int row=grid.Rows.Add(backup.AppName,created,backup.Entries.Count);grid.Rows[row].Tag=file;}catch{skipped++;}
   }status.Text=grid.Rows.Count==0?"Копий пока нет: они появятся после первой очистки реестра.":"Сохранённых копий: "+grid.Rows.Count+(skipped>0?" · Не прочитано: "+skipped:"");restore.Enabled=grid.SelectedRows.Count>0;open.Enabled=restore.Enabled;
  }catch(Exception ex){status.Text=ex.Message;}}
  async void RestoreAsync(){if(busy||grid.SelectedRows.Count==0)return;string file=(string)grid.SelectedRows[0].Tag;try{
   var backup=Store.Read<Backup>(file);if(MessageBox.Show(this,"Восстановить записи реестра для «"+backup.AppName+"»?\n\nСуществующие разделы и параметры не будут перезаписаны.","Восстановление",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
   busy=true;restore.Enabled=false;List<string> errors;
   if(backup.Entries.Any(e=>e.Target.Hive==RegistryHive.LocalMachine)&&!Store.Admin){var child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--restore \""+file+"\""){UseShellExecute=true,Verb="runas"});await Task.Run(()=>child.WaitForExit());status.Text="Повышенный процесс завершил восстановление. Результат показан в его окне.";}
   else{errors=await Task.Run(()=>CleanupEngine.RestoreBackup(file));MessageBox.Show(this,errors.Count==0?"Записи реестра восстановлены.":"Восстановление завершено с пропусками:\n\n"+String.Join("\n",errors.Take(8)),"Восстановление",MessageBoxButtons.OK,errors.Count==0?MessageBoxIcon.Information:MessageBoxIcon.Warning);}
  }catch(System.ComponentModel.Win32Exception ex){status.Text=ex.NativeErrorCode==1223?"Запрос администратора отменён.":ex.Message;}catch(Exception ex){MessageBox.Show(this,ex.Message,"Восстановление");}finally{busy=false;restore.Enabled=grid.SelectedRows.Count>0;}}
 }
}
