using System.Security.Cryptography;
using NMSE.IO;
using NMSE.Models;
namespace NMSE.Next.Engine;
public sealed partial class SaveSession
{
 public JsonObject? AccountData {get;private set;}
 public string? AccountPath {get;private set;}
 public string? AccountLoadMessage {get;private set;}
 public bool AccountDirty {get;private set;}
 byte[]? _accountHash;
 void LoadAccountCompanion(string source)
 {
  AccountData=null;AccountPath=null;AccountDirty=false;AccountLoadMessage=null;_accountHash=null;
  string path=Path.Combine(Path.GetDirectoryName(source)!,"accountdata.hg");
  if(!File.Exists(path)){AccountLoadMessage="No accountdata.hg is present beside this save.";return;}
  try
  {
   var hash=SHA256.HashData(File.ReadAllBytes(path));var data=SaveFileManager.LoadSaveFile(path);
   if(!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))))throw new IOException("Account data changed while loading.");
   AccountData=data;AccountPath=path;_accountHash=hash;
  }
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or NMSE.Models.JsonException){AccountLoadMessage=ex.Message;}
 }
 void ValidateAccountSource()
 {
  if(!AccountDirty)return;
  if(AccountPath==null||AccountData==null||_accountHash==null)throw new InvalidOperationException("Account data is unavailable.");
  if(!_accountHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(AccountPath))))throw new IOException("Account data changed on disk. Reload before saving rewards.");
 }
}
