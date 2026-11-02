module Program

open System
open System.IO
open System.Text
open MSNRBFEditor.Core

let private usage () =
    eprintfn "Usage: ms-nrbf-editor <savefile.sav> [output.json] [--compact]"
    eprintfn ""
    eprintfn "  If output.json is omitted the JSON is written to stdout."
    eprintfn "  --compact writes the JSON on a single line."

[<EntryPoint>]
let main argv =
    let arguments = argv |> Array.toList

    if arguments.IsEmpty || arguments |> List.contains "--help" || arguments |> List.contains "-h" then
        usage ()
        if arguments.IsEmpty then 1 else 0
    else
        let compact = arguments |> List.contains "--compact"
        let positional =
            arguments |> List.filter (fun arg -> not (arg.StartsWith("-", StringComparison.Ordinal)))

        match positional with
        | [] ->
            usage ()
            1
        | inputPath :: rest ->
            let outputPath =
                match rest with
                | output :: _ -> Some output
                | [] -> None

            try
                let document = SaveDocument.loadFromFile inputPath
                let json = SaveDocument.toJson document
                let text = JsonWriter.serialize (not compact) json

                match outputPath with
                | Some path -> File.WriteAllText(path, text, UTF8Encoding(false))
                | None -> Console.Out.Write(text)

                0
            with ex ->
                eprintfn "Error: %s" ex.Message
                1
