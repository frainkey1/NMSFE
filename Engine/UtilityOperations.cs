using NMSE.Models;
namespace NMSE.Next.Engine;
public sealed partial class SaveSession
{
 public void ApplyRawSubtree(object parent, object key, string json)
 {
  if(SourcePath==null)throw new InvalidOperationException("Open a save first.");
  bool Attached(object? value){if(ReferenceEquals(value,parent))return true;if(value is JsonObject o){foreach(var n in o.Names())if(Attached(o.Get(n)))return true;}else if(value is JsonArray a){for(int i=0;i<a.Length;i++)if(Attached(a.Get(i)))return true;}return false;}
  if(!Attached(Data))throw new InvalidOperationException("This branch has changed. Reopen the JSON page and select it again.");
  // Validate with the strict parser first; the game's parser retains original numeric tokens.
  using var validation=System.Text.Json.JsonDocument.Parse(json,new(){MaxDepth=256});
  var value=JsonParser.ParseValue(json);
  if(parent is JsonObject obj && key is string name)obj.Set(name,value);
  else if(parent is JsonArray arr && key is int index)arr.Set(index,value);
  else throw new ArgumentException("Select a field in the tree.");
  DocumentRevision++;Dirty=true;RefreshTools();
 }
}
