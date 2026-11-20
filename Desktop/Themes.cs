using System.Windows;
using System.Windows.Media;
namespace NMSE.Next.Desktop;
internal static class Themes
{
 internal static bool Dark {get;private set;}
 static Color C(string s)=>(Color)ColorConverter.ConvertFromString(s);
 internal static void Apply(bool dark)
 {
  Dark=dark;var r=Application.Current.Resources;
  var colors=dark?new Dictionary<string,string>{{"Canvas","#101214"},{"Panel","#1B1E20"},{"Sidebar","#171A1C"},{"Input","#101416"},{"Text","#A6DCD6"},{"Secondary","#7BA9A5"},{"Edge","#424F50"},{"Accent","#65B9B1"},{"Selection","#294B4B"},{"OnAccent","#C1EBE5"}}:new Dictionary<string,string>{{"Canvas","#BDE6EC"},{"Panel","#CCEAF0"},{"Sidebar","#B5DFE7"},{"Input","#E9F6F8"},{"Text","#082F3D"},{"Secondary","#456F79"},{"Edge","#79B3C0"},{"Accent","#0091B0"},{"Selection","#78CBD8"},{"OnAccent","#FFFFFF"}};
  foreach(var pair in colors){var b=new SolidColorBrush(C(pair.Value));b.Freeze();r[pair.Key]=b;}
  Brush Gradient(string a,string b){var brush=new LinearGradientBrush(C(a),C(b),90);brush.Freeze();return brush;}
  r["Surface"]=dark?Gradient("#2A2E30","#1B1E20"):Gradient("#DDF2F5","#B7DEE6");
  r["AccentSurface"]=dark?Gradient("#2B6260","#0E3031"):Gradient("#007C94","#03586E");
  r["Header"]=dark?Gradient("#2C3032","#141719"):Gradient("#0091B0","#034D61");
 }
}


