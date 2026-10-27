using System.Globalization;
using System.IO;
using Echo.Core;
using Echo.Recorder;
namespace Echo.App;
internal static class ClipExport
{
    private static string N(double n)=>n.ToString("0.######",CultureInfo.InvariantCulture);
    internal static void Save(MediaClip clip,double start,double end,string? inputs,string? encoder,InputOverlayPreset preset,string output,string cache,Func<bool> cancel)
    {
        if(!File.Exists(clip.Path)||!double.IsFinite(start)||!double.IsFinite(end)||start<0||end-start<.1)throw new ArgumentException("Choose a valid clip range.");
        if(string.Equals(Path.GetFullPath(clip.Path),Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a different file from the original.");
        if(cancel())throw new OperationCanceledException();
        Directory.CreateDirectory(cache);var id=Guid.NewGuid().ToString("N");var ass=Path.Combine(cache,id+".ass");var partial=output+"."+id+".partial";
        try
        {
            var args=new List<string>{"-hide_banner","-v","error","-n","-threads","1","-ss",N(start),"-i",clip.Path,"-t",N(end-start),"-map","0:v:0","-map","0:a?"};
            if(preset.Enabled)
            {
                var size=MediaTools.VideoSize(clip.Path);File.WriteAllText(ass,InputOverlayExport.Build(clip,start,end,inputs,preset,size.Width,size.Height));
                var escaped=ass.Replace('\\','/').Replace(":","\\:").Replace("'","'\\''");args.AddRange(["-vf","subtitles=filename='"+escaped+"'"]);
            }
            args.AddRange(MediaTools.VideoArguments(encoder));args.AddRange(["-c:a","aac","-b:a","192k","-movflags","+faststart","-f","mp4",partial]);
            MediaTools.Run("ffmpeg",args,900,cancel:cancel);if(Math.Abs(MediaTools.Duration(partial)-(end-start))>.3)throw new InvalidDataException("The export duration did not match your clip.");File.Move(partial,output,true);
        }
        finally{foreach(var path in new[]{partial,ass})if(File.Exists(path))File.Delete(path);}
    }
}
