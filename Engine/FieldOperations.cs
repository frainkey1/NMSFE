using System.Globalization;
using NMSE.Models;
namespace NMSE.Next.Engine;
public sealed record FieldEdit(object Parent,object Key,string Text,string Baseline);
public sealed partial class SaveSession
{
 public long DocumentRevision {get;private set;}
 public static object? ReadField(object parent,object key)=>parent switch{JsonObject o when key is string name=>o.Get(name),JsonArray a when key is int index=>a.Get(index),_=>throw new ArgumentException("Invalid field target")};
 public static string FieldText(object? value)=>value is string text?text:value is bool b?b?"true":"false":value==null?"null":JsonParser.Serialize(value,false,true);
 public bool IsAttached(object parent,bool account=false)
 {
  object? current=parent;object? root=account?AccountData:Data;
  for(int depth=0;current!=null&&depth<1024;depth++)
  {
   if(ReferenceEquals(current,root))return true;
   current=current switch{JsonObject o=>o.Parent,JsonArray a=>a.Parent,_=>null};
  }
  return false;
 }
 public void ApplyFields(IEnumerable<FieldEdit> fields,bool account=false,JsonObject? shipContext=null,bool validateOnly=false)
 {
  var prepared=new List<(FieldEdit Edit,object? Value)>();
  foreach(var edit in fields)
  {
   if(edit.Text==edit.Baseline)continue;
   if(!IsAttached(edit.Parent,account))throw new InvalidOperationException("This record changed. Reopen the page before editing it.");
   var old=ReadField(edit.Parent,edit.Key);
   if(FieldText(old)!=edit.Baseline)throw new InvalidOperationException("This value changed elsewhere. Reopen the record before applying this draft.");
   object? value;
   if(old is string)value=edit.Text;
   else if(old is bool){if(!bool.TryParse(edit.Text,out bool parsed))throw new ArgumentException("Enter true or false.");value=parsed;}
   else if(old==null){if(edit.Text.Trim()!="null")throw new ArgumentException("A null field must stay null in the form editor. Use Raw JSON for a type change.");value=null;}
   else if(old is JsonObject or JsonArray)throw new ArgumentException("Open the nested record before editing it.");
   else
   {
    using var valid=System.Text.Json.JsonDocument.Parse(edit.Text);
    if(valid.RootElement.ValueKind!=System.Text.Json.JsonValueKind.Number)throw new ArgumentException("Enter a valid JSON number.");
    value=JsonParser.ParseValue(edit.Text);
    if(value is RawDouble raw && !double.IsFinite(raw.Value))throw new ArgumentException("Numbers must be finite.");
   }
   prepared.Add((edit,value));
  }
  if(validateOnly)return;
  foreach(var (edit,value) in prepared)
  {
   if(edit.Parent is JsonObject o)o.Set((string)edit.Key,value);
   else ((JsonArray)edit.Parent).Set((int)edit.Key,value);
   if(shipContext!=null && edit.Parent is JsonObject changed && edit.Key is string key)
   {
    // Match the original ship editor's coupled class and base-stat writes.
    if(key=="InventoryClass")
     foreach(string inventory in new[]{"Inventory","Inventory_TechOnly","Inventory_Cargo"})shipContext.GetObject(inventory)?.GetObject("Class")?.Set("InventoryClass",value);
    if(key=="Value" && changed.GetString("BaseStatID") is {} id)
     foreach(string inventory in new[]{"Inventory","Inventory_TechOnly"})
     {
      var stats=shipContext.GetObject(inventory)?.GetArray("BaseStatValues");
      if(stats!=null)for(int i=0;i<stats.Length;i++)if(stats.GetObject(i) is {} stat && stat.GetString("BaseStatID")==id)stat.Set("Value",value);
     }
   }
  }
  if(prepared.Count>0){Dirty=true;if(account)AccountDirty=true;}
 }
}
