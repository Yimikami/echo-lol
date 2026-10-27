namespace Echo.Core;

public sealed record ActivityPoint(double Time,int Apm);
public static class InputActivity
{
    // Bounded chart data, computed once per selected recording rather than during playback rendering.
    public static IReadOnlyList<ActivityPoint> Build(IReadOnlyList<InputSample> samples,MediaClip clip,double start,double end,int maxPoints=512,double windowSeconds=10)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||end<=start||maxPoints<2||!double.IsFinite(windowSeconds)||windowSeconds<=0)return [];
        var presses=samples.Where(s=>s.Down).Select(s=>s.Time).Order().ToArray();
        var count=(int)Math.Min(maxPoints,Math.Max(2,Math.Ceiling(end-start)+1));
        var points=new ActivityPoint[count];int low=0,high=0;
        for(int i=0;i<count;i++)
        {
            var time=start+(end-start)*i/(count-1);var at=GameplayInputs.RecordingTime(clip,time);
            while(high<presses.Length&&presses[high]<=at)high++;
            while(low<high&&presses[low]<=at-windowSeconds)low++;
            points[i]=new(time,(int)Math.Round((high-low)*60/windowSeconds));
        }
        return points;
    }
}
