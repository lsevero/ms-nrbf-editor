module MSNRBFEditor.Gui.Program

open System
open System.IO
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform.Storage
open Avalonia.Themes.Fluent
open MSNRBFEditor.Core

type MainWindow(initialPath: string option) as this =
    inherit Window()

    let mutable document : SaveDocument option = None
    let mutable originalBytes : byte[] option = None
    let mutable sourcePath : string option = None
    let mutable entries : (string * SerializedValue) list = []
    let baseline = Dictionary<string, string>()
    let drafts = Dictionary<string, string>()
    let mutable currentKey : string option = None
    let mutable suppressTextChanged = false

    let keyList = ListBox()
    let editor = TextBox()
    let status = TextBlock()
    let currentKeyLabel = TextBlock()
    let openButton = Button(Content = "Open .sav…")
    let saveButton = Button(Content = "Save As…")
    let applyButton = Button(Content = "Apply")
    let revertButton = Button(Content = "Revert")

    do
        this.Title <- "MS-NRBF Editor"
        this.Width <- 1100.0
        this.Height <- 760.0
        this.MinWidth <- 820.0
        this.MinHeight <- 520.0

        editor.AcceptsReturn <- true
        editor.TextWrapping <- TextWrapping.NoWrap
        editor.FontFamily <- FontFamily("DejaVu Sans Mono, monospace")
        editor.FontSize <- 13.0
        editor.MinHeight <- 280.0

        currentKeyLabel.FontWeight <- FontWeight.Bold
        currentKeyLabel.Margin <- Thickness(12.0, 0.0, 0.0, 0.0)
        currentKeyLabel.Text <- "No file loaded"

        status.TextWrapping <- TextWrapping.Wrap
        status.Margin <- Thickness(8.0)
        status.Text <- "Open a .sav file to begin."

        let toolbar = StackPanel()
        toolbar.Orientation <- Orientation.Horizontal
        toolbar.Spacing <- 8.0
        toolbar.Margin <- Thickness(8.0)
        toolbar.Children.Add(openButton) |> ignore
        toolbar.Children.Add(saveButton) |> ignore
        toolbar.Children.Add(currentKeyLabel) |> ignore

        let keyHeader = TextBlock()
        keyHeader.Text <- "Save keys"
        keyHeader.FontWeight <- FontWeight.Bold
        keyHeader.Margin <- Thickness(8.0, 8.0, 8.0, 4.0)

        let leftPanel = DockPanel()
        leftPanel.Margin <- Thickness(8.0)
        DockPanel.SetDock(keyHeader, Dock.Top)
        leftPanel.Children.Add(keyHeader) |> ignore
        leftPanel.Children.Add(keyList) |> ignore

        let editorHeader = TextBlock()
        editorHeader.Text <- "Value (JSON)"
        editorHeader.FontWeight <- FontWeight.Bold
        editorHeader.Margin <- Thickness(8.0, 8.0, 8.0, 4.0)

        let buttonPanel = StackPanel()
        buttonPanel.Orientation <- Orientation.Horizontal
        buttonPanel.Spacing <- 8.0
        buttonPanel.Margin <- Thickness(0.0, 8.0, 0.0, 8.0)
        buttonPanel.Children.Add(applyButton) |> ignore
        buttonPanel.Children.Add(revertButton) |> ignore

        let rightPanel = DockPanel()
        rightPanel.Margin <- Thickness(8.0)
        DockPanel.SetDock(editorHeader, Dock.Top)
        DockPanel.SetDock(buttonPanel, Dock.Bottom)
        rightPanel.Children.Add(editorHeader) |> ignore
        rightPanel.Children.Add(buttonPanel) |> ignore
        rightPanel.Children.Add(editor) |> ignore

        let contentGrid = Grid()
        contentGrid.ColumnDefinitions <- ColumnDefinitions("300,*")
        contentGrid.Children.Add(leftPanel) |> ignore
        Grid.SetColumn(rightPanel, 1)
        contentGrid.Children.Add(rightPanel) |> ignore

        let root = DockPanel()
        DockPanel.SetDock(toolbar, Dock.Top)
        DockPanel.SetDock(status, Dock.Bottom)
        root.Children.Add(toolbar) |> ignore
        root.Children.Add(status) |> ignore
        root.Children.Add(contentGrid) |> ignore
        this.Content <- root

        openButton.Click.Add(fun _ -> this.OpenFileAsync() |> Async.StartImmediate)
        saveButton.Click.Add(fun _ -> this.SaveAsAsync() |> Async.StartImmediate)
        applyButton.Click.Add(fun _ -> this.ApplyCurrent() |> ignore)
        revertButton.Click.Add(fun _ -> this.RevertCurrent())
        keyList.SelectionChanged.Add(fun _ -> this.OnSelectionChanged())
        editor.TextChanged.Add(fun _ -> this.OnEditorTextChanged())

        match initialPath with
        | Some path -> this.LoadPath(path) |> ignore
        | None -> ()

    member this.LoadPath(path: string) : bool =
        try
            let bytes = File.ReadAllBytes(path)
            let parsed = SaveDocument.load(bytes)
            this.SetDocument(parsed, bytes, path)
            this.Title <- sprintf "MS-NRBF Editor — %s" (Path.GetFileName(path))
            status.Text <- sprintf "Loaded %s (%d bytes)" path bytes.Length
            true
        with ex ->
            status.Text <- sprintf "Could not open file: %s" ex.Message
            false

    member private _.SetDocument(newDocument: SaveDocument, bytes: byte[], path: string) =
        document <- Some newDocument
        originalBytes <- Some bytes
        sourcePath <- Some path
        entries <- SaveDocument.topLevelEntries newDocument
        baseline.Clear()
        drafts.Clear()

        let converter = JsonConverter(newDocument.Objects)

        for key, value in entries do
            let text = JsonWriter.serialize true (converter.ConvertValue value)
            baseline.[key] <- text
            drafts.[key] <- text

        keyList.ItemsSource <- entries |> List.map fst

        if entries.IsEmpty then
            currentKey <- None
            currentKeyLabel.Text <- "No values"
            editor.Text <- ""
            keyList.SelectedIndex <- -1
        else
            keyList.SelectedIndex <- 0
            this.ShowKey(0)

    member private this.ShowKey(index: int) =
        if index >= 0 && index < entries.Length then
            let key = fst entries.[index]
            currentKey <- Some key
            currentKeyLabel.Text <- key
            suppressTextChanged <- true
            editor.Text <-
                match drafts.TryGetValue(key) with
                | true, text -> text
                | _ -> ""
            suppressTextChanged <- false
            status.Text <- sprintf "Editing %s" key

    member private this.OnSelectionChanged() =
        let index = keyList.SelectedIndex
        if index >= 0 && index < entries.Length then
            this.ShowKey(index)

    member private this.OnEditorTextChanged() =
        if not suppressTextChanged then
            match currentKey with
            | Some key -> drafts.[key] <- editor.Text
            | None -> ()

    member private this.ApplyCurrent() : bool =
        match document, currentKey with
        | Some doc, Some key ->
            try
                let edited = JsonParser.parse editor.Text
                SaveDocument.applyTopLevelEdit doc key edited
                drafts.[key] <- editor.Text
                status.Text <- sprintf "Applied changes to %s" key
                true
            with ex ->
                status.Text <- sprintf "Could not apply %s: %s" key ex.Message
                false
        | _ ->
            status.Text <- "No value is selected."
            false

    member private this.RevertCurrent() =
        match currentKey with
        | Some key ->
            match baseline.TryGetValue(key) with
            | true, text ->
                suppressTextChanged <- true
                editor.Text <- text
                suppressTextChanged <- false
                drafts.[key] <- text
                status.Text <- sprintf "Reverted %s to the original value" key
            | _ -> ()
        | None -> ()

    member private this.OpenFileAsync() : Async<unit> =
        async {
            let savType = FilePickerFileType("MS-NRBF save")
            savType.Patterns <- [| "*.sav" |]

            let options = FilePickerOpenOptions()
            options.Title <- "Open MS-NRBF save"
            options.AllowMultiple <- false
            options.FileTypeFilter <- [| savType |]

            let! files = this.StorageProvider.OpenFilePickerAsync(options) |> Async.AwaitTask

            match files |> Seq.tryHead with
            | Some file ->
                match file.TryGetLocalPath() with
                | null -> status.Text <- "Could not resolve the selected local path."
                | path -> this.LoadPath(path) |> ignore
            | None -> ()
        }

    member private this.SaveAsAsync() : Async<unit> =
        async {
            match document, originalBytes, sourcePath with
            | Some _, Some bytes, Some inputPath ->
                match currentKey with
                | Some key -> drafts.[key] <- editor.Text
                | None -> ()

                let savType = FilePickerFileType("MS-NRBF save")
                savType.Patterns <- [| "*.sav" |]

                let suggestedName =
                    let name = Path.GetFileNameWithoutExtension(inputPath)
                    name + "_edited.sav"

                let options = FilePickerSaveOptions()
                options.Title <- "Save edited save as…"
                options.SuggestedFileName <- suggestedName
                options.DefaultExtension <- "sav"
                options.ShowOverwritePrompt <- true
                options.FileTypeChoices <- [| savType |]

                let! file = this.StorageProvider.SaveFilePickerAsync(options) |> Async.AwaitTask

                match file with
                | null -> ()
                | file ->
                    match file.TryGetLocalPath() with
                    | null -> status.Text <- "Could not resolve the selected local path."
                    | outputPath ->
                        let fullInput = Path.GetFullPath(inputPath)
                        let fullOutput = Path.GetFullPath(outputPath)
                        let comparison =
                            if OperatingSystem.IsWindows() then
                                StringComparison.OrdinalIgnoreCase
                            else
                                StringComparison.Ordinal

                        if String.Equals(fullInput, fullOutput, comparison) then
                            status.Text <-
                                "Refusing to overwrite the original .sav file. Choose a new file name."
                        else
                            try
                                // Start from a clean parse of the original bytes, then apply
                                // every editor draft to that graph.
                                let fresh = SaveDocument.load(bytes)

                                for key, _ in entries do
                                    match drafts.TryGetValue(key) with
                                    | true, text ->
                                        try
                                            let edited = JsonParser.parse text
                                            SaveDocument.applyTopLevelEdit fresh key edited
                                        with ex ->
                                            failwithf "Cannot apply %s: %s" key ex.Message
                                    | _ -> ()

                                let outputBytes = SaveDocument.save(fresh)

                                // Validate the generated MS-NRBF stream before writing it.
                                ignore (SaveDocument.toJson(SaveDocument.load(outputBytes)))

                                File.WriteAllBytes(outputPath, outputBytes)
                                status.Text <-
                                    sprintf "Saved %s (%d bytes). The original file was left untouched." outputPath outputBytes.Length
                            with ex ->
                                status.Text <- sprintf "Could not save: %s" ex.Message
            | _ -> status.Text <- "Open a .sav file first."
        }

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let initialPath =
                Environment.GetCommandLineArgs()
                |> Array.skip 1
                |> Array.tryFind (fun arg -> not (arg.StartsWith("-", StringComparison.Ordinal)))
            desktop.MainWindow <- MainWindow(initialPath)
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

[<EntryPoint>]
let main argv =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(argv)
