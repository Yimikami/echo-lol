using System.Media;
using System.Text;

namespace Echo.Recorder;

internal sealed class CaptureFixture : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly SoundPlayer _sound;
    private readonly MemoryStream _wave;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public CaptureFixture(int frequency)
    {
        Text = $"Echo Fixture {frequency}"; ClientSize = new(960, 540); BackColor = System.Drawing.Color.FromArgb(19, 25, 24); DoubleBuffered = true;
        _wave = new();
        using (var w = new BinaryWriter(_wave, Encoding.ASCII, true))
        {
            int count = 48000 * 5;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(48000); w.Write(96000); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 2);
            for (int i = 0; i < count; i++) w.Write((short)(Math.Sin(i * frequency * 2 * Math.PI / 48000) * 6000));
        }
        _wave.Position = 0; _sound = new(_wave); _sound.PlayLooping();
        _timer.Tick += (_, _) => Invalidate(); _timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var font = new System.Drawing.Font("Segoe UI", 28);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(166, 223, 198));
        e.Graphics.DrawString($"Echo capture fixture\n{_clock.Elapsed:hh\\:mm\\:ss\\.fff}\nMoving video + isolated test tone", font, brush, 40, 40);
        e.Graphics.FillRectangle(brush, 40 + (float)(_clock.Elapsed.TotalSeconds * 100 % 750), 350, 80, 80);
    }
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); _sound.Stop(); _sound.Dispose(); _wave.Dispose(); } base.Dispose(disposing); }
}
