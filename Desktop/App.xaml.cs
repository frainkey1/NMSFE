using System.Diagnostics;
using System.IO;
using System.Windows;
using NMSE.Next.Engine;
using NMSE.Data;

namespace NMSE.Next.Desktop;

public partial class App : Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  UiLocalizer.Register();
  SplashWindow? splash=null;
  Stopwatch? splashTimer=null;
  try
  {
   splash=new SplashWindow();
   var splashFirstFrame=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   splash.ContentRendered+=(_,_)=>splashFirstFrame.TrySetResult(true);
   MainWindow=splash;
   splash.Show();
   await splashFirstFrame.Task;
   splashTimer=Stopwatch.StartNew();
   string root=ResolveResourceRoot();
   var session=await Task.Run(()=>{var loaded=new SaveSession(root);loaded.LoadDatabase();return loaded;});
   var window=new MainWindow(session);
   var firstFrame=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   window.ContentRendered+=(_,_)=>firstFrame.TrySetResult(true);
   await KeepSplashVisibleAtLeast(splashTimer);
   MainWindow=window;
   window.Show();
   await firstFrame.Task;
   splash.Close();
  }
  catch(Exception ex)
  {
   await KeepSplashVisibleAtLeast(splashTimer);
   splash?.Close();
   MessageBox.Show(UiStrings.LocalizeText("NMSFE could not start.")+"\n\n"+UiStrings.LocalizeText(ex.Message),UiStrings.LocalizeText("NMSFE startup error"),MessageBoxButton.OK,MessageBoxImage.Error);
   Shutdown(-1);
  }
 }

 static async Task KeepSplashVisibleAtLeast(Stopwatch? timer)
 {
  if(timer==null)return;
  var remaining=TimeSpan.FromSeconds(SplashWindow.MinimumDisplaySeconds)-timer.Elapsed;
  if(remaining>TimeSpan.Zero)await Task.Delay(remaining);
 }

 static string ResolveResourceRoot()
 {
  string root=Path.Combine(AppContext.BaseDirectory,"Resources");
  if(Directory.Exists(root))return root;
  for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
  {
   string candidate=Path.Combine(d.FullName,"Resources");
   if(Directory.Exists(candidate))return candidate;
  }
  return root;
 }
}
