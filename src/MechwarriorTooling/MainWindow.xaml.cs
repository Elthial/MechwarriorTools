using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

namespace MechwarriorTooling;

public partial class MainWindow : Window
{
    private GameInstallation? installation;
    private CombatDefinitions? definitions;
    private string definitionWarning = "";
    private AssetDocument? document;
    private ModelBank? bank;
    private SoundBank? sounds;
    private Scene scene = new();
    private readonly Dictionary<string, Scene> poseCache = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private double lastTime, activeSeconds, yaw = 0.4, tilt = 0.2, zoom = 1;
    private bool ready, changingNavigation, paused, turntablePaused, cycleAnimations = true, wireframe, axes;
    private bool softenMapWhite = true;
    private int page, mechIndex, resourceIndex, sequenceIndex, frameIndex, requestId;
    private string selectedBank = "terpck.tbl", selectedGrid = "tile0.grd";
    private string? workingAssetPath;
    private ComboBox? animationPicker;
    private ComboBox? resourcePicker;
    private EgaPalette egaPalette = EgaPalette.Initial;
    private string selectedPaletteName = "tropic.pal";
    private Slider? frameSlider;
    private TextBlock? frameLabel;
    private byte[]? mapHeights;
    private WorldPlacement[] placements = [];
    private int mapColumns = 47, mapRows = 87;
    private int selectedScenario = -1;
    private RasterImage? latestImage;
    private readonly MediaPlayer player = new();
    private string? wavePath;
    private Slider? soundProgress;
    private TextBlock? soundTime;
    private bool repeatSound, draggingSound;
    private double soundDuration;
    private readonly List<string> temporaryWaves = [];

    public MainWindow()
    {
        InitializeComponent(); ready = true;
        timer.Tick += Tick; timer.Start();
        player.MediaEnded += (_, _) => { if (repeatSound) { player.Position = TimeSpan.Zero; player.Play(); } };
        player.MediaFailed += (_, e) => StatusText.Text = "Audio playback failed: " + e.ErrorException.Message;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            timer.Stop(); player.Close();
            foreach (string path in temporaryWaves)
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        };
        ConfigurePage();
    }
    private void ChooseInstallation(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the original MechWarrior installation" };
        if (dialog.ShowDialog(this) == true) OpenInstallation(dialog.FolderName);
    }
    private void OpenInterchange(object sender, RoutedEventArgs e) => new InterchangeWindow(installation?.Root) { Owner = this }.Show();
    public async void OpenInstallation(string directory)
    {
        if (!ConfirmLeave()) return;
        int request = ++requestId;
        try
        {
            StatusText.Text = "Reading installation and reviewed executable tables…";
            var loaded = await Task.Run(() =>
            {
                var game = new GameInstallation(directory); CombatDefinitions? tables = null; string warning = "";
                try { tables = CombatDefinitions.Load(game.FilePath("BTECH.EXE")); }
                catch (Exception error) when (IsAssetError(error)) { warning = error.Message; }
                return (game, tables, warning);
            });
            if (request != requestId) return;
            installation = loaded.game; definitions = loaded.tables; definitionWarning = loaded.warning;
            document = null; bank = null; workingAssetPath = null; poseCache.Clear();
            InstallationLabel.Text = installation.Root;
            ConfigurePage();
        }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
    private static bool IsAssetError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or FormatException or InvalidOperationException or OverflowException or NotSupportedException or IndexOutOfRangeException;
    private void ShowError(Exception error)
    {
        StatusText.Text = error.Message;
        MessageBox.Show(this, error.Message, "Asset could not be processed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private bool ConfirmLeave()
    {
        if (document?.Modified != true) return true;
        return MessageBox.Show(this, "There are unsaved changes. Discard them and continue? Use Save copy first to keep them.",
            "Unsaved asset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!ConfirmLeave()) e.Cancel = true; else requestId++; }
    private void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || changingNavigation) return;
        if (!ConfirmLeave())
        {
            changingNavigation = true; Navigation.SelectedIndex = page; changingNavigation = false; return;
        }
        page = Navigation.SelectedIndex; document = null; bank = null; sounds = null; workingAssetPath = null; StopSound();
        yaw = 0.4; tilt = page == 2 ? -0.8 : 0.2; zoom = 1;
        ConfigurePage();
    }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(6), Foreground = Brushes.LightSteelBlue };
    private static Button Button(string text, Action click)
    {
        var button = new Button { Content = text }; button.Click += (_, _) => click(); return button;
    }
    private static ComboBox Picker(IEnumerable<string> values, int selected, Action<int> change, Func<bool>? approve = null)
    {
        var combo = new ComboBox { ItemsSource = values.ToArray(), SelectedIndex = selected };
        int previous = selected; bool restoring = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (restoring || combo.SelectedIndex < 0 || combo.SelectedIndex == previous) return;
            if (approve != null && !approve()) { restoring = true; combo.SelectedIndex = previous; restoring = false; return; }
            previous = combo.SelectedIndex; change(combo.SelectedIndex);
        };
        return combo;
    }
    private static CheckBox Toggle(string text, bool selected, Action<bool> change)
    {
        var check = new CheckBox { Content = text, IsChecked = selected };
        check.Checked += (_, _) => change(true); check.Unchecked += (_, _) => change(false); return check;
    }
    private void ConfigurePage()
    {
        ++requestId; HeaderControls.Children.Clear(); EditorPanel.Children.Clear();
        document = null; bank = null; sounds = null;
        animationPicker = null; resourcePicker = null; frameSlider = null; frameLabel = null; soundProgress = null; soundTime = null;
        scene = new(); mapHeights = null; placements = []; latestImage = null; LeftImage.Source = null; RightImage.Source = null;
        activeSeconds = turnSeconds = 0; completedAnimationCycles = 0; lastTime = stopwatch.Elapsed.TotalSeconds; poseCache.Clear();
        worldEditing = false; terrainBank = null; effectSequence = -1;
        string[] titles = ["BattleMechs", "Model banks", "Combat maps", "Visual effects", "Sounds"];
        PageTitle.Text = titles[page];
        PageDescription.Text = page == 4 ? "IBM event bank · bounded previews · original files read-only"
            : "EGA palette · inspection rendering · original files read-only";
        LeftCaption.Text = page == 2 ? "TOP-DOWN WORLD VIEW · click a GRD cell to inspect" : "STATIC POSE · drag to rotate / wheel to zoom";
        RightCaption.Text = page == 2 ? "TERRAIN / OBJECTS · drag to rotate / wheel to zoom" : "TURNTABLE · 10 seconds per revolution";
        ChangesText.Text = "No file selected.";
        if (installation == null)
        {
            SetProperties(("Installation", "Open your legally owned MechWarrior installation."), ("Pages", "Mechs · Models · Maps · SFX · Sounds"));
            EvidenceText.Text = "No game assets are bundled with this application. All changes are saved to separate working copies.";
            return;
        }
        if (workingAssetPath != null)
        {
            HeaderControls.Children.Add(Label("Working copy: " + Path.GetFileName(workingAssetPath)));
            HeaderControls.Children.Add(Button("Back to originals", () => { if (ConfirmLeave()) { workingAssetPath = null; document = null; ConfigurePage(); } }));
        }
        if (page == 0)
        {
            if (definitions == null) { StatusText.Text = definitionWarning; SetProperties(("Chassis definitions unavailable", definitionWarning)); return; }
            HeaderControls.Children.Add(Button("❮", () => SelectMech((mechIndex + 7) % 8)));
            var selector = Picker(definitions.Mechs.Select(mech => mech.Name), mechIndex, index =>
            {
                mechIndex = index; sequenceIndex = frameIndex = 0; ConfigurePage();
            }, ConfirmLeave);
            HeaderControls.Children.Add(selector);
            HeaderControls.Children.Add(Button("❯", () => SelectMech((mechIndex + 1) % 8)));
            BuildAnimationControls();
        }
        else if (page is 1 or 3)
        {
            string[] banks = installation.Files.Where(file => file.Name.EndsWith("pck.tbl", StringComparison.OrdinalIgnoreCase)).Select(file => file.Name).ToArray();
            if (banks.Length == 0) { SetProperties(("Models", "No model banks found.")); return; }
            if (page == 3)
            {
                var effectsBank = banks.FirstOrDefault(name => name.Equals("othpck.tbl", StringComparison.OrdinalIgnoreCase));
                if (effectsBank == null) { SetProperties(("SFX", "Missing othpck.tbl; unrelated geometry is not substituted.")); return; }
                selectedBank = effectsBank;
            }
            int bankIndex = Array.FindIndex(banks, name => name.Equals(selectedBank, StringComparison.OrdinalIgnoreCase));
            if (bankIndex < 0) { bankIndex = 0; selectedBank = banks[0]; }
            if (page == 3) HeaderControls.Children.Add(Label(selectedBank));
            else HeaderControls.Children.Add(Picker(banks, bankIndex, index =>
            {
                selectedBank = banks[index]; resourceIndex = 0; workingAssetPath = null; document = null; ConfigurePage();
            }, ConfirmLeave));
            HeaderControls.Children.Add(Button("❮ resource", () => SelectResource(resourceIndex - 1)));
            HeaderControls.Children.Add(Button("resource ❯", () => SelectResource(resourceIndex + 1)));
            if (page == 1) HeaderControls.Children.Add(Button("Objective building", () =>
            {
                if (!ConfirmLeave()) return;
                selectedBank = "othpck.tbl"; resourceIndex = ModelCatalog.ObjectiveBuildingResource; workingAssetPath = null; ConfigurePage();
            }));
            if (page == 3 && definitions != null)
            {
                string[] effects = ["Single resource", .. definitions.EffectSequences.Select(effect => effect.Name)];
                HeaderControls.Children.Add(Picker(effects, 0, index => { effectSequence = index - 1; activeSeconds = 0; poseCache.Clear(); RefreshProperties(); BuildEditor(); RenderViews(); }));
            }
        }
        else if (page == 2)
        {
            HeaderControls.Children.Add(Toggle("Soften white (preview)", softenMapWhite, value => { softenMapWhite = value; RefreshProperties(); RenderViews(); }));
            string[] grids = installation.Files.Where(file => file.Name.EndsWith(".grd", StringComparison.OrdinalIgnoreCase)).Select(file => file.Name).ToArray();
            if (grids.Length == 0) { SetProperties(("Maps", "No GRD files found.")); return; }
            int gridIndex = Array.FindIndex(grids, name => name.Equals(selectedGrid, StringComparison.OrdinalIgnoreCase));
            if (gridIndex < 0) { gridIndex = 0; selectedGrid = grids[0]; }
            HeaderControls.Children.Add(Picker(grids, gridIndex, index =>
            {
                selectedGrid = grids[index]; selectedScenario = -1; workingAssetPath = null; document = null; ConfigurePage();
            }, ConfirmLeave));
            HeaderControls.Children.Add(Picker(["Individual tile", .. Enumerable.Range(0, 60).Select(id => $"Scenario {id:D2}")], selectedScenario + 1, index =>
            {
                selectedScenario = index - 1; workingAssetPath = null; document = null; ConfigurePage();
            }, ConfirmLeave));
        }
        if (page != 4)
        {
            string[] palettes = installation.Files.Where(file => file.Category == "Palette" && (page != 2 ||
                file.Name.Equals("arctic.pal", StringComparison.OrdinalIgnoreCase) || file.Name.Equals("desert.pal", StringComparison.OrdinalIgnoreCase)
                || file.Name.Equals("tropic.pal", StringComparison.OrdinalIgnoreCase))).Select(file => file.Name).ToArray();
            if (palettes.Length > 0)
            {
                int current = Array.FindIndex(palettes, name => name.Equals(selectedPaletteName, StringComparison.OrdinalIgnoreCase));
                if (current < 0) { current = 0; selectedPaletteName = palettes[0]; }
                HeaderControls.Children.Add(Label("EGA palette"));
                HeaderControls.Children.Add(Picker(palettes, current, index => ChangePalette(palettes[index])));
            }
            HeaderControls.Children.Add(Toggle("Pause animation", paused, value => paused = value));
            HeaderControls.Children.Add(Toggle("Pause twirl", turntablePaused, value => turntablePaused = value));
            HeaderControls.Children.Add(Toggle("Wireframe", wireframe, value => { wireframe = value; RenderViews(); }));
            HeaderControls.Children.Add(Toggle("Axes", axes, value => { axes = value; RenderViews(); }));
            HeaderControls.Children.Add(Button("Reset view", () => { yaw = 0.4; tilt = page == 2 ? -0.8 : 0.2; zoom = 1; RenderViews(); }));
        }
        LoadSelection();
    }
    private int effectSequence = -1;
    private void SelectMech(int index)
    {
        if (!ConfirmLeave()) return;
        mechIndex = index; sequenceIndex = 0; frameIndex = 0; document = null; ConfigurePage();
    }
    private void SelectResource(int index)
    {
        if (bank == null) return;
        resourceIndex = (index % bank.Resources.Length + bank.Resources.Length) % bank.Resources.Length;
        if (resourcePicker != null) resourcePicker.SelectedIndex = resourceIndex;
        poseCache.Clear(); RefreshProperties(); BuildEditor(); RenderViews();
    }
    private void BuildAnimationControls()
    {
        if (definitions == null) return;
        var sequences = definitions.Sequences(mechIndex); sequenceIndex = Math.Min(sequenceIndex, sequences.Length - 1);
        animationPicker = Picker(sequences.Select(sequence => sequence.Name), sequenceIndex, index =>
        {
            sequenceIndex = index; frameIndex = 0; activeSeconds = 0; completedAnimationCycles = 0; SetFrameRange(); RefreshProperties(); RenderViews();
        });
        HeaderControls.Children.Add(animationPicker);
        HeaderControls.Children.Add(Toggle("Cycle sequences", cycleAnimations, value => cycleAnimations = value));
        frameSlider = new Slider { Minimum = 0, Width = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
        frameSlider.ValueChanged += (_, _) => { frameIndex = (int)frameSlider.Value; RefreshProperties(); RenderViews(); };
        HeaderControls.Children.Add(frameSlider);
        frameLabel = Label("Frame 0"); HeaderControls.Children.Add(frameLabel); SetFrameRange();
    }
    private void SetFrameRange()
    {
        if (definitions == null || frameSlider == null) return;
        frameSlider.Maximum = definitions.Sequences(mechIndex)[sequenceIndex].ResourceFrames.Length - 1; frameSlider.Value = frameIndex;
    }
    private async void LoadSelection()
    {
        if (installation == null) return;
        int request = ++requestId, requestedPage = page;
        var game = installation;
        string name = page == 0 ? definitions!.Mechs[mechIndex].BankName : selectedBank;
        int scenario = selectedScenario; string grid = selectedGrid;
        string? working = workingAssetPath;
        try
        {
            StatusText.Text = "Loading original asset…";
            if (page != 4 && game.Files.Any(file => file.Name.Equals(selectedPaletteName, StringComparison.OrdinalIgnoreCase)))
            {
                string paletteName = selectedPaletteName;
                string palettePath = game.FilePath(paletteName);
                var loadedPalette = await Task.Run(() => EgaPalette.Load(palettePath));
                if (request != requestId) return;
                if (selectedPaletteName == paletteName) egaPalette = loadedPalette;
            }
            else egaPalette = EgaPalette.Initial;
            var result = await Task.Run(() =>
            {
                if (requestedPage is 0 or 1 or 3)
                {
                    string path = working ?? game.FilePath(name); var model = ModelBank.Load(path);
                    if (model.Resources.Length == 0) throw new InvalidDataException("Model bank has no resources.");
                    return new LoadedAsset(new AssetDocument(path, game.Root, model.Decoded, true), model, null, null, [], null);
                }
                if (requestedPage == 2)
                {
                    if (scenario >= 0)
                    {
                        var selectors = ScenarioData.Parse(BinaryData.ReadFile(game.FilePath("snario.dat")))[scenario];
                        byte[] heights = new byte[94 * 174]; var objects = new List<WorldPlacement>();
                        for (int slot = 0; slot < 4; slot++)
                        {
                            string tile = "tile" + selectors.Tiles[slot];
                            byte[] source = TerrainTile.Parse(BinaryData.ReadFile(game.FilePath(tile + ".grd"))).Heights;
                            for (int row = 0; row < 87; row++) source.AsSpan(row * 47, 47).CopyTo(heights.AsSpan((row + slot % 2 * 87) * 94 + slot / 2 * 47, 47));
                            objects.AddRange(TerrainTile.ParsePlacements(BinaryData.ReadFile(game.FilePath(tile + ".wld"))));
                        }
                        return new LoadedAsset(null, null, heights, null, [.. objects], selectors);
                    }
                    bool worldCopy = working != null && Path.GetExtension(working).Equals(".wld", StringComparison.OrdinalIgnoreCase);
                    string path = working == null ? game.FilePath(grid) : worldCopy ? Path.ChangeExtension(working, ".grd") : working;
                    byte[] bytes = BinaryData.ReadFile(path); TerrainTile.Parse(bytes);
                    string world = Path.ChangeExtension(path, ".wld");
                    var worldPlacements = File.Exists(world) ? TerrainTile.ParsePlacements(BinaryData.ReadFile(world)) : [];
                    return new LoadedAsset(new AssetDocument(worldCopy ? world : path, game.Root,
                        worldCopy ? BinaryData.ReadFile(world) : bytes, false), null, bytes, null, worldPlacements, null);
                }
                var sound = new SoundBank(BinaryData.ReadFile(game.FilePath("btechsnd.snd")));
                return new LoadedAsset(null, null, null, sound, [], null);
            });
            if (request != requestId) return;
            document = result.Document; bank = result.Bank; sounds = result.Sounds; placements = result.Placements;
            worldEditing = requestedPage == 2 && working != null && Path.GetExtension(working).Equals(".wld", StringComparison.OrdinalIgnoreCase);
            mapHeights = result.Heights; mapColumns = scenario >= 0 ? 94 : 47; mapRows = scenario >= 0 ? 174 : 87;
            if (bank != null) resourceIndex = Math.Min(resourceIndex, bank.Resources.Length - 1);
            if (bank != null && page is 1 or 3)
            {
                string catalogBank = Path.GetFileName(document!.SourcePath);
                resourcePicker = Picker(bank.Resources.Select(resource => $"{resource.Id:D3} · {ModelCatalog.Name(catalogBank, resource.Id, definitions)}"), resourceIndex,
                    index => { if (index != resourceIndex) SelectResource(index); });
                HeaderControls.Children.Add(resourcePicker);
            }
            if (page == 2 && mapHeights != null)
            {
                scene = Scene.Terrain(mapHeights, mapColumns, mapRows, CurrentLandscape);
                var terrain = await Task.Run(() => ModelBank.Load(game.FilePath("terpck.tbl")));
                if (request != requestId) return;
                terrainBank = terrain;
                foreach (var placement in placements)
                    if (placement.ResourceId < terrain.Resources.Length)
                        scene.AddResource(terrain.Resources[placement.ResourceId], new(placement.PositionA, placement.PositionB, placement.Altitude));
            }
            if (page == 4) BuildSoundControls();
            RefreshProperties(); BuildEditor(); RenderViews();
            StatusText.Text = page == 4 ? "All shipped IBM events use the recovered interpreter and the captured DOSBox-X combat-memory profile."
                : $"Loaded · {egaPalette.Name} · native EGA alternate fields and palette patterns. Projection/LOD fidelity remains separate.";
        }
        catch (Exception error) when (IsAssetError(error)) { if (request == requestId) { StatusText.Text = error.Message; SetProperties(("Asset warning", error.Message)); } }
    }
    private sealed record LoadedAsset(AssetDocument? Document, ModelBank? Bank, byte[]? Heights, SoundBank? Sounds, WorldPlacement[] Placements, ScenarioSelection? Scenario);
    private void SetProperties(params (string Property, string Value)[] rows) => PropertiesGrid.ItemsSource = rows.Select(row => new { row.Property, row.Value }).ToArray();
    private void RefreshProperties()
    {
        var rows = new List<(string, string)> { ("Installation", installation?.Root ?? "Not selected"), ("Changes", document?.Modified == true ? "Unsaved edits" : "Original / unchanged") };
        if (page != 4) rows.Add(("EGA palette", egaPalette.Name));
        if (bank != null)
        {
            int id = page == 0 ? CurrentParts(frameIndex)[0] : resourceIndex;
            if (page == 3 && effectSequence >= 0 && definitions != null) id = definitions.EffectSequences[effectSequence].Resources[0];
            var resource = bank.Resources[id];
            rows.Add(("Model", ModelCatalog.Name(document!.SourcePath, id, definitions)));
            rows.Add(("Identification evidence", ModelCatalog.Evidence(document.SourcePath, id, definitions)));
            var partUsage = definitions?.FindModelResource(document.SourcePath, id);
            if (partUsage != null)
            {
                rows.Add(("Body part", partUsage.BodyPart));
                foreach (var uses in partUsage.Uses.GroupBy(use => use.SequenceId))
                    rows.Add(($"Animation {uses.Key}: {uses.First().Animation}", string.Join(", ", uses.Select(use => $"frame {use.LogicalFrame} → shape {use.PhysicalFrame}"))));
            }
            rows.AddRange([("Source", Path.GetFileName(document!.SourcePath)), ("Resources", bank.Resources.Length.ToString()), ("Selected resource", $"{id} (0x{id:X4})"),
                ("Entries / variants", $"{resource.Entries.Length} / {resource.Entries.Sum(entry => entry.Variants.Length)}"),
                ("Vectors (includes normals)", resource.Entries.Sum(entry => entry.Vectors.Length).ToString()),
                ("Primitives", string.Join(", ", resource.Entries.SelectMany(entry => entry.Variants).SelectMany(variant => variant.Primitives).Select(primitive => primitive.Kind).Distinct()))]);
            if (page == 0 && definitions != null)
            {
                var mech = definitions.Mechs[mechIndex]; var sequence = definitions.Sequences(mechIndex)[sequenceIndex];
                rows.AddRange([("Chassis / tonnage", $"{mech.Name} · {mech.Tonnage} tonnes"), ("Heat sinks", mech.HeaderWords[8].ToString()),
                    ("Jump jet groups", mech.HeaderWords[4].ToString()), ("Maximum speed (native scale)", mech.HeaderWords[3].ToString()),
                    ("Weapons", string.Join(", ", mech.Weapons.Select(mount => mount.Weapon))), ("Maximum armour", string.Join(" / ", mech.Armour)),
                    ("Internal structure", string.Join(" / ", mech.InternalStructure)), ("Sequence", sequence.Name),
                    ("Logical / physical frame", $"{frameIndex} / {sequence.ResourceFrames[frameIndex]}"), ("Six part resources", string.Join(", ", CurrentParts(frameIndex))),
                    ("Animation rate", "8 frames/s inspection cadence; not game simulation timing")]);
            }
            EvidenceText.Text = """
                Native archive methods 00/01/02; paragraph-relative references; vectors include normals.

                EGA colour path: 22B6:6501 selects polygon +3/+4. Native 2A36:0071 fills with +4 and 0608 outlines with +3, despite the old field labels. PAL:EGA data is loaded/selected as 2A36:21BF/222D: hardware colour translation, virtual colours 16..31 and alternating row/column patterns. No four-bit truncation of virtual colours.

                The Models objective shortcut selects OTHPCK.TBL resource 000E, created by native 0810:1A2E as the special target. Its EGA faces are yellow and black, not the primary blue/grey fields.

                Animation selection: 0810:9C06; common part origin: 0810:0817. Six part bindings: left arm, right arm, positioned left leg, right leg, torso, head. Resources are part-major within physical animation blocks, not six consecutive IDs per frame. The information pane lists every logical animation/frame using the selected shape. OTH/TER visual names are supplied by the user; unknown resources remain unnamed. Projection still uses variant 0 / primitive 0 and an orthographic painter sort, not full native LOD, face ordering, clipping or primary-mode shading. Colour/pattern fixes do not claim complete pixel equivalence.

                Saving emits native method 00 with unchanged decoded offsets/unknown bytes. Vertex edits retain authored normals. SFX resource sequences are authored; procedural laser/rocket trajectories and native perspective ellipse projection remain separate work.
                """;
        }
        if (mapHeights != null)
        {
            rows.AddRange([("Grid", selectedScenario < 0 ? Path.GetFileName(workingAssetPath ?? selectedGrid) : $"Scenario {selectedScenario:D2} · four tiles"), ("Dimensions", $"{mapColumns} columns × {mapRows} rows"),
                ("Stored height range", $"{mapHeights.Min()}–{mapHeights.Max()}"), ("Height conversion", "Stored height × 16 native units"),
                ("Placed objects", placements.Length.ToString()), ("World axes", "row=(A+87×512)/512; column=(47×512−B)/512"),
                ("Editor", selectedScenario < 0 ? "Individual GRD cells; WLD placement editor below" : "Assembled preview; choose an individual tile to edit")]);
            rows.AddRange([("Landscape", CurrentLandscape.Name), ("Native sky / ground indices", $"{CurrentLandscape.Sky} / {CurrentLandscape.Ground}")]);
            rows.Add(("White display", softenMapWhite ? "Soft white #E0E0E0 (preview only; exports use this view)" : "Native EGA white #FFFFFF"));
            EvidenceText.Text = "GRD: 87×47 collision-height bytes; WLD: 14-byte records (u16 resource + three i32 coordinates). Native 0810:2438 loads without adding tile-origin offsets; 0810:5565 supplies grid/world mapping. The viewer uses a flat ground plane spanning all cells, with authored hills/rivers placed above it, not an invented GRD visual mesh. The right-hand plane and geometry share one mouse-controlled orbit/zoom camera; neutral space surrounds the finite plane. The fixed top-down view retains GRD cell picking. EGA landscape colours come from 304E:1E88 and matching PAL:EGA banks. Native briefing 0810:4176 renders world objects; these remain orthographic inspection views, not pixel-identical native projection.\n\nScenario geometry uses the four native tile positions. Live mission objectives/units are not invented. Collision planes are not visual meshes.";
        }
        if (sounds != null) rows.AddRange([("Bank", "btechsnd.snd / IBM"), ("Events", sounds.Events.Length.ToString()), ("Backend", "PC speaker"), ("Memory profile", DosToneMemoryProfile.ProfileName), ("Tick rate", $"{SoundBank.TickRate:F6} Hz"), ("Event flow", sounds.Events[soundIndex].Flow), ("Commands", string.Join(", ", sounds.Events[soundIndex].Commands))]);
        SetProperties([.. rows]); ChangesText.Text = document?.DescribeChanges() ?? "This preview is read-only.";
    }
    private int[] CurrentParts(int frame) => definitions!.PartResources(mechIndex, definitions.Sequences(mechIndex)[sequenceIndex], frame);
    private LandscapeColours CurrentLandscape => (definitions ?? throw new InvalidDataException("Reviewed BTECH.EXE is required for native map landscape colours."))
        .LandscapeForPalette(selectedPaletteName);
    private async void ChangePalette(string name)
    {
        if (installation == null) return;
        int request = requestId; selectedPaletteName = name;
        try
        {
            string path = installation.FilePath(name); var loaded = await Task.Run(() => EgaPalette.Load(path));
            if (request != requestId || selectedPaletteName != name) return;
            egaPalette = loaded;
            if (page == 2 && mapHeights != null) RebuildTerrain();
            RefreshProperties(); RenderViews();
            if (page == 2) StatusText.Text = $"{CurrentLandscape.Name} · native ground {CurrentLandscape.Ground} · {egaPalette.Name} · inspection projection.";
        }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
}
