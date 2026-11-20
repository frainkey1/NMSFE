using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
namespace NMSE.Next.Desktop;

// Original, resolution-independent pictograms drawn on a common 24-unit canvas.
internal static class ConceptIcons
{
 internal static FrameworkElement Make(string name,double size=20,Brush? color=null)
 {
  string data=name switch
  {
   "Save" => "M4,3 H17 L21,7 V21 H3 V3 Z M7,3 V10 H17 V3 M7,21 V14 H17 V21 M14,5 V8",
   "Load" => "M4,15 V21 H20 V15 M12,3 V16 M7,11 L12,16 17,11",
   "Open" => "M3,8 V5 H10 L12,8 H21 L18,20 H3 Z M3,11 H20",
   "Generate" => "M5,4 L20,7 17,21 2,18 Z M8,9 h0.1 M15,11 h0.1 M7,16 h0.1",
   "Export" => "M13,4 H21 V12 M21,4 L10,15 M9,5 H3 V21 H19 V15",
   "Primary" => "M12,2 L15,8 22,9 17,14 18,21 12,18 6,21 7,14 2,9 9,8 Z",
   "Player" => "M8,7 A4,4 0 1 0 16,7 A4,4 0 1 0 8,7 M3,22 V19 C3,12 21,12 21,19 V22",
   "Exosuit" => "M7,3 H17 L20,9 V21 H4 V9 Z M7,3 V10 H17 V3 M8,14 H16 V19 H8 Z M1,9 H4 M20,9 H23",
   "Multi-tools" => "M3,7 H15 L17,9 H22 V13 H16 L14,15 H12 L10,21 H7 L8,14 H3 Q2,14 2,13 V8 Q2,7 3,7 Z",
   "Starships" => "M12,2 C9,5 8.5,10 9,16 H15 C15.5,10 15,5 12,2 Z M9,11 L4,17 V20 L9,17 M15,11 L20,17 V20 L15,17 M10,19 L12,23 14,19 M10.5,9 A1.5,1.5 0 1 0 13.5,9 A1.5,1.5 0 1 0 10.5,9",
   "Fleet" => "M12,2 L16,12 H8 Z M5,11 L9,21 H1 Z M19,11 L23,21 H15 Z M12,14 V22",
   "Corvettes" => "M12,2 C7,5 6,10 7,16 H17 C18,10 17,5 12,2 Z M7,10 L1,15 V20 L8,18 M17,10 L23,15 V20 L16,18 M9,19 L12,23 15,19 M10,9 A2,2 0 1 0 14,9 A2,2 0 1 0 10,9",
   "Exocraft" => "M5,7 H17 L21,14 V18 H3 V12 Z M8,7 V12 H19 M5,18 A3,3 0 1 0 11,18 A3,3 0 1 0 5,18 M15,18 A3,3 0 1 0 21,18 A3,3 0 1 0 15,18",
   "Companions" => "M7,17 C7,13 10,11 12,12 C15,11 18,14 18,17 C18,22 14,19 12,19 C10,19 7,22 7,17 Z M3,8 A2,3 0 1 0 7,8 A2,3 0 1 0 3,8 M8,5 A2,3 0 1 0 12,5 A2,3 0 1 0 8,5 M14,5 A2,3 0 1 0 18,5 A2,3 0 1 0 14,5 M19,9 A2,3 0 1 0 23,9 A2,3 0 1 0 19,9",
   "Bases & Storage" => "M2,11 L12,3 22,11 M5,9 V21 H19 V9 M9,21 V14 H15 V21",
   "Space Station" => "M8,3 H16 L20,7 V17 L16,21 H8 L4,17 V7 Z M8,6 H16 V18 H8 Z M4,9 H2 V15 H4 M20,9 H22 V15 H20 M9,2 V4 M15,2 V4 M9,20 V22 M15,20 V22",
   "Catalogue" => "M12,6 C9,3 5,3 2,4 V20 C6,19 9,19 12,22 C15,19 18,19 22,20 V4 C18,3 15,3 12,6 Z M12,6 V22 M5,8 L9,9 M15,9 L19,8 M5,12 L9,13 M15,13 L19,12",
   "Milestones" => "M7,3 H17 V9 C17,16 7,16 7,9 Z M7,5 H2 V8 C2,12 5,13 8,12 M17,5 H22 V8 C22,12 19,13 16,12 M12,15 V20 M7,22 H17 M9,20 H15",
   "Settlements" => "M2,22 V11 H9 V22 M9,22 V4 H17 V22 M17,22 V9 H22 V22 M12,8 H14 M12,12 H14 M12,16 H14 M4,15 H6 M19,13 H20",
   "ByteBeats" => "M9,17 V4 L21,2 V15 M9,8 L21,6 M3,18 A3,2 0 1 0 9,18 A3,2 0 1 0 3,18 M15,16 A3,2 0 1 0 21,16 A3,2 0 1 0 15,16",
   "Account Rewards" => "M3,9 H21 V14 H3 Z M5,14 V22 H19 V14 M12,9 V22 M12,9 C2,9 4,0 9,4 Z M12,9 C22,9 20,0 15,4 Z",
   "Export Settings" => "M3,5 H21 M3,12 H21 M3,19 H21 M7,2 V8 M16,9 V15 M10,16 V22",
   "Raw JSON Editor" => "M9,3 H7 C4,3 6,10 2,12 C6,14 4,21 7,21 H9 M15,3 H17 C20,3 18,10 22,12 C18,14 20,21 17,21 H15",
   "Database Search" => "M2,5 C2,1 18,1 18,5 C18,9 2,9 2,5 Z M2,5 V17 C2,21 11,21 14,19 M2,11 C2,14 10,15 14,12 M18,5 V10 M15,15 A4,4 0 1 0 23,15 A4,4 0 1 0 15,15 M21,18 L24,22",
   "More" => "M3,12 A1,1 0 1 0 5,12 A1,1 0 1 0 3,12 M11,12 A1,1 0 1 0 13,12 A1,1 0 1 0 11,12 M19,12 A1,1 0 1 0 21,12 A1,1 0 1 0 19,12",
   "Less" => "M5,15 L12,8 19,15",
   _ => "M5,9 L12,16 19,9"
  };
  var path=new Path {Data=Geometry.Parse(data),StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=size,Height=size,IsHitTestVisible=false,SnapsToDevicePixels=true};
  if(name=="Multi-tools")
  {
   path.Tag="FilledIcon";path.Stroke=null;
   if(color!=null)path.Fill=color;else path.SetResourceReference(Shape.FillProperty,"Text");
  }
  else if(color!=null)path.Stroke=color;else path.SetResourceReference(Shape.StrokeProperty,"Text");
  return path;
 }
}
