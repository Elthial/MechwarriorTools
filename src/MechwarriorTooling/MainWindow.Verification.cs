using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MechwarriorTooling;

public partial class MainWindow
{
    /// <summary>Renders this application's controls without showing a window or taking desktop control.</summary>
    internal async Task VerifyUiAsync(string gameDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        // Render the content tree detached from the never-shown Window. Hidden
        // Window visuals intentionally produce a transparent bitmap in WPF.
        var content = (FrameworkElement)Content; Content = null;
        async Task WaitFor(Func<bool> complete)
        {
            var deadline = Stopwatch.StartNew();
            while (!complete())
            {
                if (deadline.Elapsed.TotalSeconds > 20) throw new TimeoutException(StatusText.Text);
                await Task.Delay(30);
            }
        }
        async Task Capture(string name)
        {
            content.Measure(new Size(1360, 900)); content.Arrange(new Rect(0, 0, 1360, 900)); content.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1360, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            uint[] pixels = new uint[1360 * 900]; bitmap.CopyPixels(pixels, 1360 * 4, 0);
            if (pixels.Distinct().Take(16).Count() < 16) throw new InvalidOperationException("UI rendered blank or without expected content.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(Path.Combine(outputDirectory, name + ".png"), FileMode.CreateNew, FileAccess.Write); encoder.Save(output);
        }
        OpenInstallation(gameDirectory);
        await WaitFor(() => bank != null && LeftImage.Source != null);
        await Capture("01-mechs");
        for (int next = 1; next < 5; next++)
        {
            Navigation.SelectedIndex = next;
            await WaitFor(() => next == 4 ? sounds != null : LeftImage.Source != null);
            await Capture($"0{next + 1}-{new[] { "mechs", "models", "maps", "sfx", "sounds" }[next]}");
            if (next == 1)
            {
                HeaderControls.Children.OfType<Button>().Single(button => Equals(button.Content, "Objective building"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await WaitFor(() => bank != null && resourceIndex == 14 && document != null && Path.GetFileName(document.SourcePath).Equals("othpck.tbl", StringComparison.OrdinalIgnoreCase) && LeftImage.Source != null);
                if (latestImage == null || !latestImage.Pixels.Contains(Rendering.SoftwareRenderer.Ega[14])) throw new InvalidOperationException("Objective is not yellow in EGA.");
                await Capture("objective-building-yellow-black");
                selectedBank = "locpck.tbl"; resourceIndex = 14; ConfigurePage();
                await WaitFor(() => bank != null && document != null && Path.GetFileName(document.SourcePath).Equals("locpck.tbl", StringComparison.OrdinalIgnoreCase) && resourcePicker != null && LeftImage.Source != null);
                if (resourcePicker!.SelectedItem?.ToString()?.Contains("Right arm", StringComparison.Ordinal) != true)
                    throw new InvalidOperationException("Mech shape picker is missing the native body-part label.");
                await Capture("locust-right-arm-walk-shape");
            }
            if (next == 2)
            {
                var mapDocument = document;
                foreach (var landscape in definitions!.Landscapes)
                {
                    HeaderControls.Children.OfType<ComboBox>().Single(combo => combo.Items.Contains(landscape.PaletteFile)).SelectedItem = landscape.PaletteFile;
                    await WaitFor(() => egaPalette.Name == landscape.PaletteFile && scene.BackgroundColour == landscape.Ground);
                    if (!ReferenceEquals(mapDocument, document)) throw new InvalidOperationException("Landscape switching replaced the map document.");
                    uint ground = Rendering.SoftwareRenderer.Ega[egaPalette.PolygonPixel(landscape.Ground, 0, 0)];
                    if (softenMapWhite && ground == Rendering.SoftwareRenderer.Ega[15]) ground = Rendering.SoftwareRenderer.SoftWhite;
                    if (latestImage == null || !latestImage.Pixels.Contains(ground) || latestImage.Pixels[0] != 0xFF101923)
                        throw new InvalidOperationException("Finite map plane must use native ground colours against a neutral viewport.");
                    await Capture("map-" + landscape.Name.ToLowerInvariant());
                    if (landscape.Index == 2)
                    {
                        var toggle = HeaderControls.Children.OfType<CheckBox>().Single(box => Equals(box.Content, "Soften white (preview)"));
                        toggle.IsChecked = false;
                        if (latestImage?.Pixels.Contains(Rendering.SoftwareRenderer.Ega[15]) != true) throw new InvalidOperationException("Native white toggle failed.");
                        await Capture("map-arctic-native-white");
                        toggle.IsChecked = true;
                        if (latestImage?.Pixels.Contains(Rendering.SoftwareRenderer.SoftWhite) != true) throw new InvalidOperationException("Soft white toggle failed.");
                    }
                }
                HeaderControls.Children.OfType<ComboBox>().Single(combo => combo.Items.Contains("tropic.pal")).SelectedItem = "tropic.pal";
                await WaitFor(() => egaPalette.Name == "tropic.pal");
                var beforeOrbit = latestImage!.Pixels.ToArray();
                yaw = 1.2; tilt = -0.5; zoom = 1.5; RenderViews();
                if (beforeOrbit.SequenceEqual(latestImage!.Pixels)) throw new InvalidOperationException("Map orbit/zoom did not change the plane and geometry.");
                await Capture("map-plane-orbit-zoom");
                HeaderControls.Children.OfType<Button>().Single(button => Equals(button.Content, "Reset view"))
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!beforeOrbit.SequenceEqual(latestImage!.Pixels)) throw new InvalidOperationException("Map camera reset did not restore the original view.");
            }
            if (next == 3)
            {
                HeaderControls.Children.OfType<ComboBox>().First().SelectedIndex = 1;
                activeSeconds = 0.7; RenderViews(); await Capture("sfx-impact-last-frame");
                HeaderControls.Children.OfType<ComboBox>().First().SelectedIndex = 2;
                activeSeconds = 0.2; RenderViews(); await Capture("sfx-machine-gun");
            }
        }
        // A self-contained event exercises PCM rendering without opening an audio device.
        HeaderControls.Children.OfType<ComboBox>().First().SelectedIndex = 1;
        EnsureSoundPreview(); await Capture("06-sound-waveform");
        foreach (int id in Enumerable.Range(0, 15))
        {
            HeaderControls.Children.OfType<ComboBox>().First().SelectedIndex = id;
            EnsureSoundPreview();
            if (soundPreview == null || soundPreview.Wave.Length <= 44) throw new InvalidOperationException("Sound preview failed.");
        }
        await Capture("sound-0E-recovered-modulation");
        Navigation.SelectedIndex = 0;
        for (int chassis = 1; chassis < 8; chassis++)
        {
            SelectMech(chassis);
            string expected = definitions!.Mechs[chassis].BankName;
            await WaitFor(() => document != null && bank != null && Path.GetFileName(document.SourcePath).Equals(expected, StringComparison.OrdinalIgnoreCase) && LeftImage.Source != null);
            await Capture($"mech-{chassis}-{definitions.Mechs[chassis].Name.Replace(' ', '-')}");
            if (chassis == 1)
            {
                animationPicker!.SelectedIndex = 1; frameSlider!.Value = 2;
                turnSeconds = 2.5; activeSeconds = 0.5; RenderViews();
                await Capture("mech-1-JENNER-walk-quarter-turn");
            }
            if (chassis == 2)
            {
                animationPicker!.SelectedIndex = Array.FindIndex(definitions.Sequences(chassis), sequence => sequence.Id == 2);
                frameSlider!.Value = 0; RenderViews(); await Capture("mech-2-PHOENIX-HAWK-jump-EGA");
            }
        }
        InformationTabs.SelectedIndex = 1;
        var inputs = EditorPanel.Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<TextBox>()).ToArray();
        inputs[0].Text = (short.Parse(inputs[0].Text) + 1).ToString();
        var apply = EditorPanel.Children.OfType<Button>().Single(button => Equals(button.Content, "Apply vertex coordinates"));
        apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (document?.Modified != true) throw new InvalidOperationException("Model editor failed to apply vertex change.");
        var editedDocument = document;
        ChangePalette("desert.pal");
        await WaitFor(() => egaPalette.Name == "desert.pal");
        if (!ReferenceEquals(document, editedDocument) || !document.Modified) throw new InvalidOperationException("Palette change discarded the edited document.");
        document.SaveCopy(Path.Combine(outputDirectory, "edited-model.tbl"), ValidateDocument);
        await Capture("07-model-editor");
        ChangePalette("tropic.pal");
        await WaitFor(() => egaPalette.Name == "tropic.pal");
        Undo(this, new RoutedEventArgs());
        if (document.Modified) throw new InvalidOperationException("Model undo failed.");
        Redo(this, new RoutedEventArgs());
        if (!document.Modified) throw new InvalidOperationException("Model redo failed.");
        Undo(this, new RoutedEventArgs());
        Navigation.SelectedIndex = 2;
        await WaitFor(() => mapHeights != null && document != null && LeftImage.Source != null);
        inputs = EditorPanel.Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<TextBox>()).ToArray();
        inputs[2].Text = (byte.Parse(inputs[2].Text) ^ 1).ToString();
        EditorPanel.Children.OfType<Button>().Single(button => Equals(button.Content, "Apply height")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (!document!.Modified) throw new InvalidOperationException("Height editor failed.");
        document.SaveCopy(Path.Combine(outputDirectory, "tile0.grd"), ValidateDocument);
        await Capture("08-height-editor"); Undo(this, new RoutedEventArgs());
        SwitchMapDocument(true);
        inputs = EditorPanel.Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<TextBox>()).ToArray();
        inputs[1].Text = (int.Parse(inputs[1].Text) + 1).ToString();
        EditorPanel.Children.OfType<Button>().Single(button => Equals(button.Content, "Apply placement")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (!document!.Modified) throw new InvalidOperationException("Placement editor failed.");
        document.SaveCopy(Path.Combine(outputDirectory, "tile0.wld"), ValidateDocument);
        await Capture("09-placement-editor"); Undo(this, new RoutedEventArgs());
        selectedScenario = 0; ConfigurePage();
        await WaitFor(() => mapHeights?.Length == 94 * 174 && LeftImage.Source != null);
        InformationTabs.SelectedIndex = 0; await Capture("10-assembled-scenario");
        File.WriteAllText(Path.Combine(outputDirectory, "verification.txt"), "PASS: five pages, all eight EGA chassis, Phoenix Hawk jump, labelled yellow/black objective shortcut, all fifteen sound waveforms, model/height/placement edit controls, palette switching retains edits, disk save/reopen validation, undo/redo, assembled scenario. No window shown or audio device opened.");
        var interchange = new InterchangeWindow(installation!.Root);
        await interchange.VerifyOffscreenAsync(installation, outputDirectory);
        timer.Stop(); player.Close();
    }
}
