using NMSE.Next.Engine;
using System.Security.Cryptography;
var resourceRoot=args.Length>0?args[0]:@"C:\Users\Frank Jr\Documents\AgentWorkspace\NMS-Editor\Resources";
CompanionAccessoryChecks.Run(resourceRoot);
FieldChecks.Run(resourceRoot);
SectionCatalogChecks.Run(resourceRoot);
AllianceChecks.Run();
PlayerKnowledgeChecks.Run(resourceRoot);
PlayerAdvancedChecks.Run(resourceRoot);
PlayerSlotChecks.Run(resourceRoot);
ConsoleSlotChecks.Run(resourceRoot);
ConsoleSaveChecks.Run(resourceRoot);
PlayerGeneralChecks.Run(resourceRoot);
PlayerChecks.Run(resourceRoot);
ExosuitChecks.Run(resourceRoot);
SaveWriteChecks.Run(resourceRoot);
var session=new SaveSession(resourceRoot);
var source=Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"HelloGames","NMS"),"save8.hg",SearchOption.AllDirectories).First();
session.LoadDatabase();
var before=SHA256.HashData(File.ReadAllBytes(source));
session.Load(source);
if(session.Tools.Count==0)throw new Exception("No real multi-tools loaded");
var tool=session.Tools[0];var draft=session.ReadTool(tool.Index);
var slots=session.Inventory(tool.Index)!.GetArray("Slots")!; for(int q=0;q<Math.Min(4,slots.Length);q++){var id=slots.GetObject(q)!.GetString("Id")!;var item=session.Items.GetItem(id);Console.WriteLine($"ITEM: {id} / {item?.Name} / {item?.Icon}");}
var occupied=slots.GetObject(0)!;
var originalAmount=occupied.GetInt("Amount");var originalMax=occupied.GetInt("MaxAmount");
session.ApplySlot(tool.Index,occupied,originalAmount,originalMax,occupied.GetDouble("DamageFactor"));
if(occupied.GetInt("Amount")!=originalAmount)throw new Exception("Installed tech amount sentinel changed");
var beforeInvalid=session.Data.ToString();
try{session.ApplySlot(tool.Index,occupied,originalMax+1,originalMax,0);throw new Exception("Excessive amount accepted");}catch(ArgumentException){}
if(beforeInvalid!=session.Data.ToString())throw new Exception("Failed validation changed data");
foreach(var owned in session.Tools){var inv=session.Inventory(owned.Index)!;var special=inv.GetArray("SpecialSlots");int charged=0;if(special!=null)for(int i=0;i<special.Length;i++)if(special.GetObject(i)?.GetObject("Type")?.GetString("InventorySpecialSlotType")=="TechBonus")charged++;Console.WriteLine($"STATE {owned.Name}: {charged} supercharged entries, {inv.GetInt("Width")}x{inv.GetInt("Height")}");}
var originalName=draft.Name;draft=draft with{Name="NMSE.Next roundtrip check"};session.ApplyTool(tool.Index,draft);
var output=Path.Combine(Path.GetTempPath(),"nmse-next-check-"+Guid.NewGuid().ToString("N")+".hg");
try{
 session.SaveCopy(output);
 var reopened=new SaveSession(resourceRoot);reopened.Load(output);
 if(reopened.ReadTool(tool.Index).Name!=draft.Name)throw new Exception("Tool edit did not survive compressed save/reload");
 if(!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))throw new Exception("Original save changed");
 var unchanged=reopened.Data.ToString();var noEdit=reopened.ReadTool(tool.Index);reopened.ApplyTool(tool.Index,noEdit);
 if(unchanged!=reopened.Data.ToString())throw new Exception("No-op editor apply changed unrelated raw data");
 try{reopened.ApplyTool(tool.Index,noEdit with{Seed="not-a-seed"});throw new Exception("Invalid seed accepted");}catch(ArgumentException){}
 try{session.SaveCopy(source);throw new Exception("Source overwrite accepted");}catch(InvalidOperationException){}
 Console.WriteLine($"PASS: real save loaded; {session.Tools.Count} tools; compressed edit roundtrip; original unchanged; no-op fidelity; invalid-seed validation; source overwrite protection. Original tool: {originalName}");
}finally{if(File.Exists(output))File.Delete(output);}


