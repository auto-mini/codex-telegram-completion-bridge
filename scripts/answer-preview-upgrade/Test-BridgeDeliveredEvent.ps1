#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ThreadId,
    [Parameter(Mandatory=$true)][string]$TurnId,
    [Parameter(Mandatory=$true)][string]$ExpectedAnswer,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BridgeReceiptReader {
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr stmt,IntPtr tail);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int column);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr stmt,int column);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr stmt,int column);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_int(IntPtr stmt,int column);
  [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
  public static object[] Read(string path,string id) {
    if(id.Length!=64 || !System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-f0-9]+$")) throw new ArgumentException("EVENT_HASH_INVALID");
    IntPtr db=IntPtr.Zero,stmt=IntPtr.Zero;
    try {
      if(sqlite3_open_v2(Encoding.UTF8.GetBytes(path+"\0"),out db,1,IntPtr.Zero)!=0) throw new InvalidOperationException("RECEIPT_DATABASE_OPEN_FAILED");
      sqlite3_busy_timeout(db,2000);
      var sql="SELECT state,delivery_attempt_count,completed_at_utc,delivery_envelope_dpapi FROM events WHERE event_id='"+id+"'";
      if(sqlite3_prepare_v2(db,Encoding.UTF8.GetBytes(sql+"\0"),-1,out stmt,IntPtr.Zero)!=0) throw new InvalidOperationException("RECEIPT_QUERY_FAILED");
      var rc=sqlite3_step(stmt); if(rc==101)return Array.Empty<object>();
      if(rc!=100)throw new InvalidOperationException("RECEIPT_READ_FAILED");
      var bytes=new byte[sqlite3_column_bytes(stmt,3)];
      if(bytes.Length>0)Marshal.Copy(sqlite3_column_blob(stmt,3),bytes,0,bytes.Length);
      return new object[]{Marshal.PtrToStringUTF8(sqlite3_column_text(stmt,0)),sqlite3_column_int(stmt,1),Marshal.PtrToStringUTF8(sqlite3_column_text(stmt,2)),bytes};
    } finally {if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);if(db!=IntPtr.Zero)sqlite3_close(db);}
  }
}
'@
$root=Join-Path $env:LOCALAPPDATA 'CodexTelegramBridge'
$runtime=Get-Content -LiteralPath (Join-Path $root 'config\bridge.json') -Raw|ConvertFrom-Json
$material=$runtime.machine_id+"`n"+$ThreadId+"`n"+$TurnId
$id=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($material))).ToLowerInvariant()
$row=[BridgeReceiptReader]::Read((Join-Path $root 'state\bridge-state.sqlite'),$id)
$report=[ordered]@{schema_version=1;event_hash_prefix=$id.Substring(0,12);observed_utc=[DateTimeOffset]::UtcNow.ToString('O');event_present=($row.Length-ne0)}
if($row.Length-ne0){
    $report['state']=$row[0];$report['retries']=$row[1];$report['sent_at_utc']=$row[2]
    if($row[3].Length-gt0){
        Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
        $plain=[Security.Cryptography.ProtectedData]::Unprotect([byte[]]$row[3],[Text.Encoding]::UTF8.GetBytes('CodexTelegramBridge:v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
        try {
            $envelope=[Text.Encoding]::UTF8.GetString($plain)|ConvertFrom-Json
            $previewProperty=$envelope.PSObject.Properties['answer_preview']
            $preview=if($previewProperty){[string]$previewProperty.Value}else{''}
            $expected=[regex]::Replace($ExpectedAnswer.Normalize(), '[\s\p{Cc}\u061c\u200e-\u200f\u202a-\u202e\u2066-\u2069]+',' ').Trim()
            $indexes=[Globalization.StringInfo]::ParseCombiningCharacters($expected)
            if($indexes.Length-gt50){$expected=$expected.Substring(0,$indexes[50])+'…'}
            $report['expected_preview_matches']=($preview-ceq$expected)
            $report['preview_graphemes']=[Globalization.StringInfo]::ParseCombiningCharacters($preview).Length
            $report['message_lines']=$envelope.telegram_text.Split("`n").Length
        }finally{[Array]::Clear($plain,0,$plain.Length)}
    }
}
# The report contains no IDs, credentials, PC names or actual answer text.
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output 'delivery_receipt_written=yes'
exit 0
