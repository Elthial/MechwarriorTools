using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

namespace MechwarriorTooling;

public partial class MainWindow
{
    private double turnSeconds;
    private int completedAnimationCycles;
    private Point? dragPoint;
    private int soundIndex;
    private SoundPreview? soundPreview;
    private void Tick(object? sender, EventArgs e)
    {
        double now = stopwatch.Elapsed.TotalSeconds, delta = now - lastTime; lastTime = now;
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        if (page == 4)
        {
            if (soundProgress != null && !draggingSound && soundDuration > 0) soundProgress.Value = player.Position.TotalSeconds;
            if (soundTime != null) soundTime.Text = $"{player.Position.TotalSeconds:F2} / {soundDuration:F2} s";
            return;
        }
        if (!paused) activeSeconds += delta;
        if (!turntablePaused)
        {
            int previousRevolution = (int)(turnSeconds / PreviewClock.RevolutionSeconds);
            turnSeconds += delta;
            if (!paused) completedAnimationCycles += (int)(turnSeconds / PreviewClock.RevolutionSeconds) - previousRevolution;
        }
        if (page == 2 || bank == null || (paused && turntablePaused)) return;
        try { RenderViews(); }
        catch (Exception error) when (IsAssetError(error)) { paused = turntablePaused = true; StatusText.Text = error.Message; }
    }
    private static BitmapSource Bitmap(RasterImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bitmap.Freeze(); return bitmap;
    }
    private Scene Pose(int selectedSequence, int frame)
    {
        string key = $"{selectedSequence}:{frame}";
        if (!poseCache.TryGetValue(key, out var cached))
        {
            var sequence = definitions!.Sequences(mechIndex)[selectedSequence];
            cached = Scene.FromBank(bank!, definitions.PartResources(mechIndex, sequence, frame)); poseCache[key] = cached;
        }
        return cached;
    }
    private void RenderViews()
    {
        if (page == 4) return;
        Scene left = scene, right = scene;
        if (page == 0 && bank != null && definitions != null)
        {
            var sequences = definitions.Sequences(mechIndex);
            left = Pose(sequenceIndex, frameIndex);
            int automaticSequence = (sequenceIndex + (cycleAnimations ? completedAnimationCycles % sequences.Length : 0)) % sequences.Length;
            int automaticFrame = (int)(activeSeconds * 8 % sequences[automaticSequence].ResourceFrames.Length);
            right = Pose(automaticSequence, automaticFrame);
            if (frameLabel != null) frameLabel.Text = $"Static {frameIndex} · twirl {sequences[automaticSequence].Name} {automaticFrame}";
        }
        else if (bank != null)
        {
            if (page == 3 && effectSequence >= 0 && definitions != null)
            {
                int[] frames = definitions.EffectSequences[effectSequence].Resources;
                if (frames.Length == 0) return;
                left = Scene.FromBank(bank, [frames[0]]);
                int frame = (int)(activeSeconds * 8 % frames.Length);
                string key = "effect:" + frame;
                if (!poseCache.TryGetValue(key, out right!)) { right = Scene.FromBank(bank, [frames[frame]]); poseCache[key] = right; }
                RightCaption.Text = $"AUTHORED RESOURCE SEQUENCE · frame {frame} · inspection cadence 8 fps";
            }
            else
            {
                string key = "resource:" + resourceIndex;
                if (!poseCache.TryGetValue(key, out left!)) { left = Scene.FromBank(bank, [resourceIndex]); poseCache[key] = left; }
                right = left;
                if (page == 3) RightCaption.Text = "TURNTABLE · 10 seconds per revolution";
            }
        }
        if (page == 2 && mapHeights != null)
        {
            var briefing = SoftwareRenderer.Briefing(scene, mapRows * 4, mapColumns * 4, egaPalette, softenMapWhite);
            LeftImage.Source = Bitmap(briefing);
            latestImage = SoftwareRenderer.Render(scene, 480, 360, yaw, tilt, zoom, wireframe, axes, egaPalette, softenWhite: softenMapWhite); RightImage.Source = Bitmap(latestImage);
            return;
        }
        latestImage = SoftwareRenderer.Render(left, 480, 360, yaw, tilt, zoom, wireframe, axes, egaPalette);
        LeftImage.Source = Bitmap(latestImage);
        RightImage.Source = Bitmap(SoftwareRenderer.Render(right, 480, 360, PreviewClock.Angle(turnSeconds) + 0.4, 0.2, 1, wireframe, axes, egaPalette));
    }
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (page == 4) return;
        if (page == 2 && sender == LeftImage && mapHeights != null && selectedScenario < 0 && !worldEditing)
        {
            var point = e.GetPosition(LeftImage);
            double sourceWidth = mapRows * 4, sourceHeight = mapColumns * 4;
            double scale = Math.Min(LeftImage.ActualWidth / sourceWidth, LeftImage.ActualHeight / sourceHeight);
            if (scale <= 0) return;
            double x = (point.X - (LeftImage.ActualWidth - sourceWidth * scale) / 2) / scale;
            double y = (point.Y - (LeftImage.ActualHeight - sourceHeight * scale) / 2) / scale;
            if (x >= 0 && x < sourceWidth && y >= 0 && y < sourceHeight)
            {
                selectedColumn = (int)(y / 4); selectedRow = (int)(x / 4); BuildEditor();
                StatusText.Text = $"Cell {selectedColumn}, {selectedRow}: {mapHeights[selectedRow * mapColumns + selectedColumn]} (stored).";
            }
            return;
        }
        if ((page == 2 && sender == LeftImage) || (sender == RightImage && page != 2)) return;
        dragPoint = e.GetPosition(this); ((Image)sender).CaptureMouse();
    }
    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (dragPoint is not { } previous || e.LeftButton != MouseButtonState.Pressed) return;
        Point current = e.GetPosition(this); yaw += (current.X - previous.X) * 0.01;
        tilt = Math.Clamp(tilt + (current.Y - previous.Y) * 0.01, -1.5, page == 2 ? -0.05 : 1.5); dragPoint = current; RenderViews();
    }
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e) { dragPoint = null; ((Image)sender).ReleaseMouseCapture(); }
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (page == 4 || (page == 2 && sender == LeftImage) || (sender == RightImage && page != 2)) return;
        zoom = Math.Clamp(zoom * Math.Pow(1.1, e.Delta / 120.0), 0.1, 8); RenderViews();
    }
    private void StopSound() { player.Stop(); player.Close(); soundPreview = null; wavePath = null; soundDuration = 0; }
    private void BuildSoundControls()
    {
        HeaderControls.Children.Clear();
        if (sounds == null) return;
        soundIndex = Math.Clamp(soundIndex, 0, sounds.Events.Length - 1);
        HeaderControls.Children.Add(Picker(sounds.Events.Select(sound => $"{sound.Id:X2} · {sound.Name} · {sound.Flow}"), soundIndex, index => { StopSound(); soundIndex = index; RefreshProperties(); DrawSoundTimeline(); }));
        HeaderControls.Children.Add(Button("▶ Play", PlaySound));
        HeaderControls.Children.Add(Button("Ⅱ Pause", () => player.Pause()));
        HeaderControls.Children.Add(Button("■ Stop", () => { player.Stop(); if (soundProgress != null) soundProgress.Value = 0; }));
        HeaderControls.Children.Add(Toggle("Repeat", repeatSound, value => repeatSound = value));
        HeaderControls.Children.Add(Button("Export WAV…", ExportWave));
        soundProgress = new Slider { Width = 200, Minimum = 0, Maximum = 10, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        soundProgress.PreviewMouseDown += (_, _) => draggingSound = true;
        soundProgress.PreviewMouseUp += (_, _) => { player.Position = TimeSpan.FromSeconds(soundProgress.Value); draggingSound = false; };
        soundProgress.PreviewKeyUp += (_, _) => player.Position = TimeSpan.FromSeconds(soundProgress.Value);
        HeaderControls.Children.Add(soundProgress); soundTime = Label("Not rendered"); HeaderControls.Children.Add(soundTime);
        LeftCaption.Text = "IBM TONE EVENT · command-flow timeline"; RightCaption.Text = "WAVEFORM · supported PC-speaker preview";
        EvidenceText.Text = "btechsnd.snd contains INF and IBM chunks; no ITM fallback chunk. All fifteen shipped IBM events use native 2134:0196 commands and 0101 period/level/modulation advancement. Missing ITM selects the proven DOSBox-X combat profile, not a replacement instrument bank: divide vector 1D3B:1F4A, captured vectors 1..5 and zero bytes 0000:1F4A..211D. Reads outside the finite profile fail.\n\nNative scheduler: 65535 / 13 PIT divisor; callback interval 1. Square-wave preview is bounded to 10 seconds; authored loops and truncation are reported separately. This profile is not a claim of identical memory on every DOS machine. No AdLib/Tandy/Roland substitution or acoustic parity is claimed.";
        DrawSoundTimeline();
    }
    private void DrawSoundTimeline()
    {
        if (sounds == null) return;
        var image = new RasterImage(480, 360);
        var commands = sounds.Events[soundIndex].Commands;
        for (int index = 0; index < commands.Length; index++)
        {
            int x = 15 + index * 440 / Math.Max(1, commands.Length);
            image.Line(x, 160, x, 200, SoftwareRenderer.Ega[11]);
        }
        LeftImage.Source = Bitmap(image); RightImage.Source = null;
        LeftCaption.Text = $"EVENT {soundIndex:X2} · unique command fields (not chronological) · {sounds.Events[soundIndex].Flow}";
    }
    private void EnsureSoundPreview()
    {
        if (soundPreview != null) return;
        soundPreview = sounds!.Render(soundIndex);
        soundDuration = soundPreview.Seconds;
        if (soundTime != null) soundTime.Text = $"0.00 / {soundDuration:F2} s";
        if (soundProgress != null) soundProgress.Maximum = Math.Max(0.01, soundDuration);
        var image = new RasterImage(480, 360); byte[] wave = soundPreview.Wave;
        int samples = (wave.Length - 44) / 2;
        for (int x = 0; x < image.Width; x++)
        {
            int first = x * samples / image.Width, last = (x + 1) * samples / image.Width;
            int minimum = 0, maximum = 0;
            for (int sample = first; sample < last; sample++)
            {
                short amplitude = (short)(wave[44 + sample * 2] | wave[45 + sample * 2] << 8);
                minimum = Math.Min(minimum, amplitude); maximum = Math.Max(maximum, amplitude);
            }
            image.Line(x, 180 - maximum / 64, x, 180 - minimum / 64, SoftwareRenderer.Ega[10]);
        }
        RightImage.Source = Bitmap(image);
        RightCaption.Text = $"PC SPEAKER · {soundDuration:F3}s · {(soundPreview.Truncated ? "bounded window" : "finite event")}";
    }
    private void PlaySound()
    {
        try
        {
            if (wavePath == null)
            {
                EnsureSoundPreview(); wavePath = Path.Combine(Path.GetTempPath(), "MechwarriorTooling-" + Guid.NewGuid().ToString("N") + ".wav");
                using (var file = new FileStream(wavePath, FileMode.CreateNew, FileAccess.Write)) file.Write(soundPreview!.Wave);
                temporaryWaves.Add(wavePath); player.Open(new Uri(wavePath));
            }
            if (player.Position.TotalSeconds >= soundDuration - 0.001) player.Position = TimeSpan.Zero;
            player.Play();
            StatusText.Text = soundPreview!.Truncated ? "Playing a bounded 10-second preview using the captured DOSBox-X profile; not a finite duration claim." : "Playing the native speaker event with the recovered DOSBox-X combat profile.";
        }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
    private void ExportWave()
    {
        try
        {
            EnsureSoundPreview();
            var dialog = new SaveFileDialog { Filter = "PCM wave|*.wav", FileName = $"event-{soundIndex:X2}.wav" };
            if (dialog.ShowDialog(this) != true) return;
            using var file = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write); file.Write(soundPreview!.Wave);
        }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
}
