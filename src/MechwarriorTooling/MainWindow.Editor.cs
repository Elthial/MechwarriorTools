using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

namespace MechwarriorTooling;

public partial class MainWindow
{
    private bool worldEditing;
    private ModelBank? terrainBank;
    private int editedPart, editedEntry, editedVertex, editedPolygon, selectedRow, selectedColumn, editedPlacement;
    private void ChooseWorkingCopy(object sender, RoutedEventArgs e)
    {
        if (installation == null) { StatusText.Text = "Open the original installation first, then choose a working copy."; return; }
        var dialog = new OpenFileDialog { Title = "Open an edited model bank, terrain tile or placement file", Filter = "Editable assets|*.tbl;*.grd;*.wld" };
        if (dialog.ShowDialog(this) != true || !ConfirmLeave()) return;
        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension == ".wld" && !File.Exists(Path.ChangeExtension(dialog.FileName, ".grd")))
        { StatusText.Text = "A working WLD needs its matching GRD alongside it for preview."; return; }
        workingAssetPath = dialog.FileName; document = null; bank = null; selectedScenario = -1; StopSound();
        page = extension == ".tbl" ? 1 : 2;
        changingNavigation = true; Navigation.SelectedIndex = page; changingNavigation = false; ConfigurePage();
    }

    private TextBox Field(string label, object value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, Width = 200, Margin = new Thickness(6), Foreground = System.Windows.Media.Brushes.LightSteelBlue });
        var input = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture), Width = 190 };
        row.Children.Add(input); EditorPanel.Children.Add(row); return input;
    }
    private void EditorAction(string title, Action edit) => EditorPanel.Children.Add(Button(title, () =>
    {
        try { edit(); ReparseEditedAsset(); } catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }));
    private static short Signed(TextBox field) => short.Parse(field.Text, CultureInfo.InvariantCulture);
    private static int Integer(TextBox field) => int.Parse(field.Text, CultureInfo.InvariantCulture);
    private void BuildEditor()
    {
        EditorPanel.Children.Clear();
        if (page == 3 && effectSequence >= 0)
        {
            EditorPanel.Children.Add(Label("Select Single resource to edit a frame's geometry. Native sequence tables embedded in BTECH.EXE remain read-only.")); return;
        }
        if (bank != null && document != null && page is 0 or 1 or 3)
        {
            EditorPanel.Children.Add(Label("Existing topology only. Vertex movement retains authored normals; normal/vector aliases cannot be edited here."));
            int resource = resourceIndex;
            if (page == 0)
            {
                var names = CombatDefinitions.BodyParts;
                EditorPanel.Children.Add(Picker(names, editedPart, index => { editedPart = index; editedEntry = editedVertex = editedPolygon = 0; BuildEditor(); }));
                resource = CurrentParts(frameIndex)[editedPart];
            }
            var model = bank.Resources[resource];
            if (model.Entries.Length == 0) { EditorPanel.Children.Add(Label("This resource has no editable render entry.")); return; }
            editedEntry = Math.Clamp(editedEntry, 0, model.Entries.Length - 1);
            EditorPanel.Children.Add(Picker(Enumerable.Range(0, model.Entries.Length).Select(index => $"Entry {index}"), editedEntry,
                index => { editedEntry = index; editedVertex = editedPolygon = 0; BuildEditor(); }));
            var entry = model.Entries[editedEntry];
            var polygons = entry.Variants.SelectMany(variant => variant.Primitives).SelectMany(primitive => primitive.Polygons).ToArray();
            var vertices = polygons.SelectMany(polygon => polygon.Indices).Distinct().Order().ToArray();
            if (vertices.Length > 0)
            {
                editedVertex = Math.Clamp(editedVertex, 0, vertices.Length - 1);
                EditorPanel.Children.Add(Picker(vertices.Select(index => $"Vertex vector {index}"), editedVertex, index => { editedVertex = index; BuildEditor(); }));
                var vertex = entry.Vectors[vertices[editedVertex]];
                var x = Field("Model X / native A", vertex.X); var y = Field("Model Y / native B", vertex.Y); var z = Field("Altitude / native C", vertex.Z);
                EditorAction("Apply vertex coordinates", () =>
                {
                    if (bank.Resources.SelectMany(item => item.Entries).Any(other => other.Variants.SelectMany(item => item.Primitives).SelectMany(item => item.Polygons)
                        .Any(face => ((face.Flags & 3) is 1 or 3) && other.Vectors[face.NormalIndex].FileOffset == vertex.FileOffset)))
                        throw new InvalidOperationException("This vector also owns a face normal. Editing that alias is unsupported.");
                    short first = Signed(x), second = Signed(y), third = Signed(z);
                    document.Change(data => { data.Write16(vertex.FileOffset, first); data.Write16(vertex.FileOffset + 2, second); data.Write16(vertex.FileOffset + 4, third); }, bytes => ModelBank.Parse(bytes));
                });
            }
            if (polygons.Length > 0)
            {
                editedPolygon = Math.Clamp(editedPolygon, 0, polygons.Length - 1);
                EditorPanel.Children.Add(Picker(Enumerable.Range(0, polygons.Length).Select(index => $"Polygon {index}"), editedPolygon, index => { editedPolygon = index; BuildEditor(); }));
                var polygon = polygons[editedPolygon];
                var fill = Field("Primary field (+1)", polygon.Fill); var line = Field("Primary field (+2)", polygon.Line);
                var egaSurface = Field("EGA surface (+4)", polygon.AlternateLine); var egaOutline = Field("EGA outline (+3)", polygon.AlternateFill);
                EditorAction("Apply polygon palette fields", () =>
                {
                    byte fillValue = checked((byte)Integer(fill)), lineValue = checked((byte)Integer(line));
                    byte egaSurfaceValue = checked((byte)Integer(egaSurface)), egaOutlineValue = checked((byte)Integer(egaOutline));
                    document.Change(data => { data.Bytes[polygon.FileOffset + 1] = fillValue; data.Bytes[polygon.FileOffset + 2] = lineValue;
                        data.Bytes[polygon.FileOffset + 3] = egaOutlineValue; data.Bytes[polygon.FileOffset + 4] = egaSurfaceValue; }, bytes => ModelBank.Parse(bytes));
                });
            }
            return;
        }
        if (page == 2 && selectedScenario < 0 && document != null)
        {
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            modes.Children.Add(Button("Edit terrain heights", () => SwitchMapDocument(false)));
            modes.Children.Add(Button("Edit object placements", () => SwitchMapDocument(true)));
            EditorPanel.Children.Add(modes);
            if (!worldEditing)
            {
                selectedColumn = Math.Clamp(selectedColumn, 0, 46); selectedRow = Math.Clamp(selectedRow, 0, 86);
                var column = Field("Column (0–46)", selectedColumn); var row = Field("Row (0–86)", selectedRow);
                var height = Field("Stored height (0–255)", document.Bytes[selectedRow * 47 + selectedColumn]);
                EditorAction("Apply height", () =>
                {
                    int c = Integer(column), r = Integer(row); byte value = checked((byte)Integer(height));
                    if ((uint)c >= 47 || (uint)r >= 87) throw new ArgumentOutOfRangeException(nameof(column), "Cell is outside the tile.");
                    selectedColumn = c; selectedRow = r;
                    document.Change(data => data.Bytes[r * 47 + c] = value, bytes => TerrainTile.Parse(bytes));
                });
            }
            else
            {
                if (placements.Length == 0) { EditorPanel.Children.Add(Label("This WLD has no existing placements; adding records is not yet supported.")); return; }
                editedPlacement = Math.Clamp(editedPlacement, 0, placements.Length - 1);
                EditorPanel.Children.Add(Picker(placements.Select((item, index) => $"Object {index} · resource {item.ResourceId}"), editedPlacement, index => { editedPlacement = index; BuildEditor(); }));
                var placement = placements[editedPlacement];
                var resource = Field("Terrain resource ID", placement.ResourceId); var a = Field("World A", placement.PositionA);
                var b = Field("World B", placement.PositionB); var altitude = Field("World altitude C", placement.Altitude);
                EditorAction("Apply placement", () =>
                {
                    int id = Integer(resource), first = Integer(a), second = Integer(b), third = Integer(altitude);
                    if (terrainBank == null || (uint)id >= terrainBank.Resources.Length) throw new InvalidOperationException("Resource ID is not in the terrain bank.");
                    document.Change(data => { data.Write16(placement.FileOffset, checked((short)id)); data.Write32(placement.FileOffset + 2, first); data.Write32(placement.FileOffset + 6, second); data.Write32(placement.FileOffset + 10, third); }, bytes => TerrainTile.ParsePlacements(bytes));
                });
            }
            return;
        }
        EditorPanel.Children.Add(Label(page == 4 ? "Sound editing is planned after native writer validation. Playback and WAV export are available for supported events."
            : "This assembled/diagnostic view is read-only. Select an individual editable asset."));
    }
    private void SwitchMapDocument(bool world)
    {
        if (installation == null || world == worldEditing || !ConfirmLeave()) return;
        try
        {
            string gridPath = workingAssetPath == null ? installation.FilePath(selectedGrid) : Path.ChangeExtension(workingAssetPath, ".grd");
            string worldPath = Path.ChangeExtension(gridPath, ".wld");
            string path = world ? worldPath : gridPath;
            byte[] bytes = BinaryData.ReadFile(path);
            if (world) TerrainTile.ParsePlacements(bytes); else TerrainTile.Parse(bytes);
            mapHeights = TerrainTile.Parse(BinaryData.ReadFile(gridPath)).Heights;
            placements = File.Exists(worldPath) ? TerrainTile.ParsePlacements(BinaryData.ReadFile(worldPath)) : [];
            worldEditing = world; document = new AssetDocument(path, installation.Root, bytes, false);
            ReparseEditedAsset();
        }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
    private void ValidateDocument(byte[] bytes)
    {
        if (document!.IsArchive) ModelBank.Parse(bytes);
        else if (worldEditing) TerrainTile.ParsePlacements(bytes);
        else TerrainTile.Parse(bytes);
    }
    private void ReparseEditedAsset()
    {
        if (document == null) return;
        poseCache.Clear();
        if (document.IsArchive) bank = ModelBank.Parse(document.Bytes);
        else if (worldEditing) placements = TerrainTile.ParsePlacements(document.Bytes);
        else mapHeights = TerrainTile.Parse(document.Bytes).Heights;
        if (page == 2 && mapHeights != null) RebuildTerrain();
        RefreshProperties(); BuildEditor(); RenderViews();
    }
    private void RebuildTerrain()
    {
        scene = Scene.Terrain(mapHeights!, mapColumns, mapRows, CurrentLandscape);
        if (terrainBank != null)
            foreach (var placement in placements)
                if (placement.ResourceId < terrainBank.Resources.Length)
                    scene.AddResource(terrainBank.Resources[placement.ResourceId], new(placement.PositionA, placement.PositionB, placement.Altitude));
    }
    private void Undo(object sender, RoutedEventArgs e) { document?.Undo(); ReparseEditedAsset(); }
    private void Redo(object sender, RoutedEventArgs e) { document?.Redo(); ReparseEditedAsset(); }
    private void SaveCopy(object sender, RoutedEventArgs e)
    {
        if (document == null) { StatusText.Text = "Choose an editable model, GRD or WLD file first."; return; }
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(document.SourcePath), Title = "Save a new working copy outside the original installation", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { document.SaveCopy(dialog.FileName, ValidateDocument); StatusText.Text = "Verified working copy saved: " + dialog.FileName; }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
    private void ExportPng(object sender, RoutedEventArgs e)
    {
        if (latestImage == null) return;
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "asset-preview.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { using var file = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write); latestImage.WritePng(file); }
        catch (Exception error) when (IsAssetError(error)) { ShowError(error); }
    }
}
