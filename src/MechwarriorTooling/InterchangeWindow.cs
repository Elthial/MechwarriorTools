using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

namespace MechwarriorTooling;

/// <summary>File-based authoring interchange, separate from the original-asset inspection pages.</summary>
public sealed class InterchangeWindow : Window
{
    private readonly string? installationRoot;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
    private readonly MediaPlayer player = new();
    private readonly List<string> temporaryAudio = [];
    private string? source;
    private string? pictureSource;
    private ModelBank? models;
    private BitmapAsset? bitmaps;
    private CampaignPictures? campaign;
    private PcmClip? pcm;
    private uint[] colours = PngInterchange.Colours(EgaPalette.Initial);
    private readonly ComboBox pictures = new() { Margin = new Thickness(6), MinWidth = 200 };
    private readonly Image picturePreview = new() { Height = 300, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    private readonly TextBox resource = new() { Text = "0", Width = 100, Margin = new Thickness(6) };
    private readonly TextBox loopStart = new() { Text = "0", Width = 100, Margin = new Thickness(6) };
    private readonly TextBox loopEnd = new() { Text = "0", Width = 100, Margin = new Thickness(6) };
    private readonly ComboBox pictureFormat = new() { ItemsSource = new[] { "Auto", "Combat BMP/SCR", "Campaign whole-bank", "Campaign indexed MW_PICS" }, SelectedIndex = 0, Margin = new Thickness(6) };
    public InterchangeWindow(string? installation)
    {
        installationRoot = installation; Title = "MechwarriorTooling · Asset interchange"; Width = 940; Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(17, 25, 35)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(10), Background = Background }; Content = root;
        status.Foreground = Brushes.LightSteelBlue;
        RenderOptions.SetBitmapScalingMode(picturePreview, BitmapScalingMode.NearestNeighbor);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var mesh = Tab(tabs, "Blender / OBJ", "Export/import mesh resources. Resource ID equal to the bank count appends geometry, not a new playable chassis. Apply Blender transforms; use Z-up and preserve mw_* material names. CLI supports assembled Mech poses and whole-bank workflows.");
        mesh.Children.Add(Action("Open native model bank…", () =>
        { var path = Open("Native models|*.tbl"); if (path == null) return; models = ModelBank.Load(path); source = path; status.Text = $"{path} · {models.Resources.Length} resources"; }));
        mesh.Children.Add(new TextBlock { Text = "Resource ID (decimal)", Margin = new Thickness(6) }); mesh.Children.Add(resource);
        mesh.Children.Add(Action("Export resource OBJ + MTL…", () =>
        {
            var bank = models ?? throw new InvalidOperationException("Open a model bank first."); int id = int.Parse(resource.Text, CultureInfo.InvariantCulture);
            string? path = Output("Wavefront OBJ|*.obj"); if (path == null) return;
            string mtl = Path.ChangeExtension(path, ".mtl"); if (File.Exists(mtl) || File.Exists(path)) throw new IOException("Choose new OBJ/MTL files.");
            var result = ObjInterchange.Export(bank, [id], Path.GetFileName(mtl), SourcePalette());
            Write(mtl, Encoding.UTF8.GetBytes(result.Mtl)); Write(path, Encoding.UTF8.GetBytes(result.Obj));
        }));
        mesh.Children.Add(Action("Import OBJ as replacement/append; save new bank…", () =>
        {
            var bank = models ?? throw new InvalidOperationException("Open a model bank first."); int id = int.Parse(resource.Text, CultureInfo.InvariantCulture);
            if (id < 0 || id > bank.Resources.Length) throw new ArgumentException("Resource must exist or equal the append slot.");
            string? input = Open("Wavefront OBJ|*.obj"); if (input == null) return;
            var imported = ObjInterchange.Import(ObjInterchange.SelectResource(Encoding.UTF8.GetString(BinaryData.ReadFile(input)).TrimStart('\uFEFF'), id), id < bank.Resources.Length ? bank.Resources[id] : null);
            byte[] decoded = NativeModelWriter.Replace(bank, id, imported); string? path = Output("Native model bank|*.tbl"); if (path != null) Write(path, NativeArchive.StoreRaw(decoded));
        }));
        var graphics = Tab(tabs, "2D / PNG", "Native indexed pixels, not screenshots. PNGs retain 16-colour palettes for Aseprite. Imported artwork must retain dimensions and native colours; no silent resizing/quantization. Combat BMP index zero is transparent.");
        graphics.Children.Add(pictureFormat);
        graphics.Children.Add(Action("Open native graphics bank/screen…", () =>
        {
            string? path = Open("Native artwork|*.bmp;*.scr;*.bin"); if (path == null) return; byte[] bytes = BinaryData.ReadFile(path);
            bool tagged = pictureFormat.SelectedIndex == 1 || (pictureFormat.SelectedIndex == 0 && bytes.Length >= 4 && (bytes.AsSpan(0, 4).SequenceEqual("BMP:"u8) || bytes.AsSpan(0, 4).SequenceEqual("SCR:"u8)));
            var bitmap = tagged ? BitmapAsset.Parse(bytes) : null;
            var picturesBank = tagged ? null : new CampaignPictures(bytes, pictureFormat.SelectedIndex == 3 || (pictureFormat.SelectedIndex == 0 && Path.GetFileName(path).Equals("mw_pics.bin", StringComparison.OrdinalIgnoreCase)));
            bitmaps = bitmap; campaign = picturesBank; source = pictureSource = path; colours = campaign?.Palette ?? PngInterchange.Colours(SourcePalette());
            pictures.ItemsSource = (bitmaps?.Pictures ?? campaign!.Pictures).Select(picture => $"{picture.Id}: {picture.Width} × {picture.Height}").ToArray(); pictures.SelectedIndex = 0;
        }));
        pictures.SelectionChanged += (_, _) =>
        {
            if (pictures.SelectedIndex < 0) return; var picture = Picture();
            byte[] png = PngInterchange.Export(picture, colours, bitmaps?.TransparentZero == true);
            using var input = new MemoryStream(png); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit();
            picturePreview.Source = bitmap;
        };
        graphics.Children.Add(pictures); graphics.Children.Add(picturePreview);
        graphics.Children.Add(Action("Export selected indexed PNG…", () => { var picture = Picture(); var path = Output("PNG image|*.png"); if (path != null) Write(path, PngInterchange.Export(picture, colours, bitmaps?.TransparentZero == true)); }));
        graphics.Children.Add(Action("Import PNG; save new native bank…", () =>
        {
            var old = Picture(); string? input = Open("PNG image|*.png"); if (input == null) return;
            var picture = PngInterchange.Import(BinaryData.ReadFile(input), old.Id, colours, bitmaps?.TransparentZero == true);
            byte[] bytes = bitmaps != null ? bitmaps.Replace(old.Id, picture) : campaign!.Replace(old.Id, picture);
            string? path = Output("Native graphics|*" + Path.GetExtension(pictureSource)); if (path != null) Write(path, bytes);
        }));
        var audio = Tab(tabs, "Sampled audio / PCM", "MWPCM v1 stores mono/stereo S16 PCM at the source rate. Loop points are sample frames [start,end). This is a NEW asset format; the game's original SND loader does not consume it yet. Preview plays the whole clip; loop points are preserved on export.");
        audio.Children.Add(Action("Open WAV or MWPCM…", () =>
        {
            string? path = Open("Sampled audio|*.wav;*.mwpcm"); if (path == null) return;
            byte[] bytes = BinaryData.ReadFile(path); pcm = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? PcmClip.ReadWave(bytes) : PcmClip.ReadNative(bytes);
            source = path; loopStart.Text = pcm.LoopStart.ToString(CultureInfo.InvariantCulture); loopEnd.Text = pcm.LoopEnd.ToString(CultureInfo.InvariantCulture);
            status.Text = $"{path} · {pcm.SampleRate} Hz · {pcm.Channels} channels · {pcm.Frames} frames · {pcm.Seconds:F2} s";
        }));
        audio.Children.Add(new TextBlock { Text = "Loop start / end (0 and 0 disables looping)", Margin = new Thickness(6) }); audio.Children.Add(loopStart); audio.Children.Add(loopEnd);
        audio.Children.Add(Action("Play", () =>
        {
            var clip = EditedPcm(); player.Close(); string temporary = Path.Combine(Path.GetTempPath(), "mwpcm-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(temporary, clip.WriteWave()); temporaryAudio.Add(temporary); player.Open(new Uri(temporary)); player.Play();
        }));
        audio.Children.Add(Action("Pause", player.Pause)); audio.Children.Add(Action("Stop", player.Stop));
        audio.Children.Add(Action("Export native MWPCM…", () => { var clip = EditedPcm(); var path = Output("Native PCM|*.mwpcm"); if (path != null) Write(path, clip.WriteNative()); }));
        audio.Children.Add(Action("Export WAV…", () => { var clip = EditedPcm(); var path = Output("WAV audio|*.wav"); if (path != null) Write(path, clip.WriteWave()); }));
        var music = Tab(tabs, "Music / MIDI", "Original .mus files use standard MIDI format 0 containers (including native real-time events). Export is byte-exact; imported MIDI must be format 0, one track and PPQN timing. MIDI instrument playback still depends on the chosen backend.");
        foreach (bool export in new[] { true, false }) music.Children.Add(Action(export ? "MUS → MIDI…" : "MIDI → MUS…", () =>
        {
            string? input = Open(export ? "Native music|*.mus" : "MIDI format 0|*.mid;*.midi"); if (input == null) return;
            byte[] bytes = MidiInterchange.Validate(BinaryData.ReadFile(input)); string? path = Output(export ? "MIDI song|*.mid" : "Native music|*.mus");
            source = input; if (path != null) Write(path, bytes);
        }));
        Closed += (_, _) => { player.Close(); foreach (string file in temporaryAudio) try { File.Delete(file); } catch (IOException) { } };
        player.MediaFailed += (_, args) => status.Text = "Audio device playback failed: " + args.ErrorException.Message;
    }
    private IndexedPicture Picture() => (bitmaps?.Pictures ?? campaign?.Pictures ?? throw new InvalidOperationException("Open a graphics file first."))
        [pictures.SelectedIndex >= 0 ? pictures.SelectedIndex : throw new InvalidOperationException("Select an image.")];
    private EgaPalette SourcePalette()
    {
        string? directory = installationRoot ?? (source == null ? null : Path.GetDirectoryName(source));
        if (directory == null) return EgaPalette.Initial;
        var game = new GameInstallation(directory);
        return game.Files.Any(file => file.Name.Equals("tropic.pal", StringComparison.OrdinalIgnoreCase)) ? EgaPalette.Load(game.FilePath("tropic.pal")) : EgaPalette.Initial;
    }
    internal async Task VerifyOffscreenAsync(GameInstallation game, string outputDirectory)
    {
        models = ModelBank.Load(game.FilePath("locpck.tbl"));
        bitmaps = BitmapAsset.Parse(BinaryData.ReadFile(game.FilePath("prompt.bmp")));
        pictures.ItemsSource = bitmaps.Pictures.Select(picture => $"{picture.Id}: {picture.Width} × {picture.Height}").ToArray(); pictures.SelectedIndex = 0;
        pcm = PcmClip.ReadWave(new SoundBank(BinaryData.ReadFile(game.FilePath("btechsnd.snd"))).Render(8).Wave);
        if (!EditedPcm().Samples.SequenceEqual(pcm.Samples) || picturePreview.Source == null) throw new InvalidOperationException("Interchange audio/image binding failed.");
        var root = (DockPanel)Content; Content = null;
        var tabs = root.Children.OfType<TabControl>().Single();
        for (int tab = 0; tab < tabs.Items.Count; tab++)
        {
            tabs.SelectedIndex = tab; root.Measure(new Size(940, 720)); root.Arrange(new Rect(0, 0, 940, 720)); root.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            var bitmap = new RenderTargetBitmap(940, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            uint[] pixels = new uint[940 * 720]; bitmap.CopyPixels(pixels, 940 * 4, 0);
            if (pixels.Distinct().Take(16).Count() < 16) throw new InvalidOperationException("Interchange UI rendered blank.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = new FileStream(Path.Combine(outputDirectory, $"interchange-{tab}.png"), FileMode.CreateNew, FileAccess.Write); encoder.Save(file);
        }
        player.Close();
    }
    private PcmClip EditedPcm()
    {
        var clip = (pcm ?? throw new InvalidOperationException("Open WAV or MWPCM first.")) with
        { LoopStart = int.Parse(loopStart.Text, CultureInfo.InvariantCulture), LoopEnd = int.Parse(loopEnd.Text, CultureInfo.InvariantCulture) }; clip.Validate(); return clip;
    }
    private static StackPanel Tab(TabControl tabs, string title, string explanation)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        System.Windows.Documents.TextElement.SetForeground(panel, Brushes.WhiteSmoke);
        panel.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    private Button Action(string title, System.Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(5), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        { try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException or NotSupportedException) { status.Text = error.Message; } }; return button;
    }
    private string? Open(string filter)
    { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    private string? Output(string filter)
    { var dialog = new SaveFileDialog { Filter = filter, OverwritePrompt = true }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    private void Write(string path, byte[] bytes)
    {
        string full = Path.GetFullPath(path);
        if (full.Equals(source, StringComparison.OrdinalIgnoreCase) || (installationRoot != null && full.StartsWith(Path.TrimEndingDirectorySeparator(installationRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Save a new file outside the original installation.");
        if (source != null)
        {
            string parent = Path.GetDirectoryName(source)!;
            if (Directory.EnumerateFiles(parent).Any(file => Path.GetFileName(file).Equals("BTECH.EXE", StringComparison.OrdinalIgnoreCase))
                && full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Export outside the original installation.");
        }
        for (var folder = Directory.GetParent(full); folder != null; folder = folder.Parent) if (folder.LinkTarget != null) throw new IOException("Output through directory links is unsupported.");
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write); stream.Write(bytes); status.Text = "Exported " + full;
    }
}
