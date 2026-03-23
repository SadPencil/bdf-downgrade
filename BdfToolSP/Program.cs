// BdfToolSP
//
// Processes BDF (Bitmap Distribution Format) font files. Supports downgrading
// BDF 2.2 files to BDF 2.1, and merging two BDF 2.1 files into one.
//
// Usage:
//   BdfToolSP downgrade [--font-name <name>] <input.bdf> <output.bdf>
//       Downgrade: converts BDF version 2.2 to version 2.1.
//       --font-name <name>  (optional) Rewrite FAMILY_NAME, FONT_NAME, and
//                           FACE_NAME properties to <name> in the output.
//
//   BdfToolSP merge --font-name <name> <input1.bdf> <input2.bdf> <output.bdf>
//       Merge: combines two BDF 2.1 files into one. Glyphs from the first file
//       take precedence when both files define the same encoding. Aborts if
//       either input is not BDF 2.1.
//       --font-name <name>  (required) Rewrite FAMILY_NAME, FONT_NAME, and
//                           FACE_NAME properties to <name> in the output.
//
// Downgrade transformations applied:
//   1. STARTFONT version line changed from 2.2 to 2.1
//   2. METRICSSET keyword (BDF 2.2-only) is removed
//   3. Global SWIDTH/DWIDTH/SWIDTH1/DWIDTH1/VVECTOR lines in the header are removed
//   4. Per-glyph SWIDTH1/DWIDTH1/VVECTOR lines (vertical metrics, BDF 2.2-only) are removed
//   5. Glyphs that relied on global SWIDTH/DWIDTH defaults get explicit SWIDTH/DWIDTH
//      lines inserted before their BBX line

using System.CommandLine;

// ── downgrade subcommand ──────────────────────────────────────────────────────

var downgradeInputArg = new Argument<string>("input.bdf")
{
    Description = "Input BDF file (version 2.2 or 2.1)"
};
var downgradeOutputArg = new Argument<string>("output.bdf")
{
    Description = "Output BDF 2.1 file"
};
var downgradeFontNameOption = new Option<string?>("--font-name")
{
    Description = "Rewrite FAMILY_NAME, FONT_NAME, and FACE_NAME to <name>"
};

var downgradeCommand = new Command("downgrade", "Convert BDF 2.2 to BDF 2.1");
downgradeCommand.Arguments.Add(downgradeInputArg);
downgradeCommand.Arguments.Add(downgradeOutputArg);
downgradeCommand.Options.Add(downgradeFontNameOption);
downgradeCommand.SetAction(parseResult =>
{
    string input = parseResult.GetValue(downgradeInputArg)!;
    string output = parseResult.GetValue(downgradeOutputArg)!;
    string? fontName = parseResult.GetValue(downgradeFontNameOption);
    return DowngradeBdfFile(input, output, fontName);
});

// ── merge subcommand ──────────────────────────────────────────────────────────

var mergeInput1Arg = new Argument<string>("input1.bdf")
{
    Description = "First BDF 2.1 font file (takes precedence on conflicts)"
};
var mergeInput2Arg = new Argument<string>("input2.bdf")
{
    Description = "Second BDF 2.1 font file"
};
var mergeOutputArg = new Argument<string>("output.bdf")
{
    Description = "Output merged BDF 2.1 file"
};
var mergeFontNameOption = new Option<string>("--font-name")
{
    Description = "Name to set for FAMILY_NAME, FONT_NAME, and FACE_NAME",
    Required = true
};

var mergeCommand = new Command("merge", "Combine two BDF 2.1 files");
mergeCommand.Arguments.Add(mergeInput1Arg);
mergeCommand.Arguments.Add(mergeInput2Arg);
mergeCommand.Arguments.Add(mergeOutputArg);
mergeCommand.Options.Add(mergeFontNameOption);
mergeCommand.SetAction(parseResult =>
{
    string input1 = parseResult.GetValue(mergeInput1Arg)!;
    string input2 = parseResult.GetValue(mergeInput2Arg)!;
    string output = parseResult.GetValue(mergeOutputArg)!;
    string fontName = parseResult.GetValue(mergeFontNameOption)!;
    return MergeBdfFiles(input1, input2, output, fontName);
});

// ── Root command ──────────────────────────────────────────────────────────────

var rootCommand = new RootCommand("BDF font file processing tool");
rootCommand.Subcommands.Add(downgradeCommand);
rootCommand.Subcommands.Add(mergeCommand);

return rootCommand.Parse(args).Invoke();

// ── Downgrade command implementation ─────────────────────────────────────────

int DowngradeBdfFile(string inputPath, string outputPath, string? fontName)
{
    // First pass: collect the global SWIDTH/DWIDTH defaults from the file header
    // (lines before STARTPROPERTIES that set defaults for glyphs missing their own).
    string? globalSwidth = null;
    string? globalDwidth = null;

    try
    {
        using (var reader = new StreamReader(inputPath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("STARTPROPERTIES", StringComparison.Ordinal))
                    break;
                if (line.StartsWith("SWIDTH ", StringComparison.Ordinal))
                    globalSwidth = line;
                if (line.StartsWith("DWIDTH ", StringComparison.Ordinal))
                    globalDwidth = line;
            }
        }

        // Second pass: write the downgraded BDF 2.1 file.
        bool inGlyph = false;
        bool glyphHasSwidth = false;

        using var inputStream = new StreamReader(inputPath);
        using var outputStream = new StreamWriter(outputPath);

        string? inputLine;
        while ((inputLine = inputStream.ReadLine()) != null)
        {
            // 1. Rewrite the version line.
            if (inputLine.StartsWith("STARTFONT ", StringComparison.Ordinal))
            {
                outputStream.WriteLine("STARTFONT 2.1");
                continue;
            }

            // 2. Drop METRICSSET (BDF 2.2-only).
            if (inputLine.StartsWith("METRICSSET", StringComparison.Ordinal))
                continue;

            // Track glyph context.
            if (inputLine.StartsWith("STARTCHAR ", StringComparison.Ordinal))
            {
                inGlyph = true;
                glyphHasSwidth = false;
            }
            else if (inputLine.StartsWith("ENDCHAR", StringComparison.Ordinal))
            {
                inGlyph = false;
            }

            // 3. Drop global header SWIDTH/DWIDTH/SWIDTH1/DWIDTH1/VVECTOR lines
            //    (they appear before any STARTCHAR and are not valid in BDF 2.1).
            if (!inGlyph &&
                (inputLine.StartsWith("SWIDTH ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("DWIDTH ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("SWIDTH1 ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("DWIDTH1 ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("VVECTOR ", StringComparison.Ordinal)))
                continue;

            // 4. Drop per-glyph vertical metrics (BDF 2.2-only).
            if (inGlyph &&
                (inputLine.StartsWith("SWIDTH1 ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("DWIDTH1 ", StringComparison.Ordinal) ||
                 inputLine.StartsWith("VVECTOR ", StringComparison.Ordinal)))
                continue;

            // Track whether this glyph has its own horizontal SWIDTH.
            if (inGlyph && inputLine.StartsWith("SWIDTH ", StringComparison.Ordinal))
                glyphHasSwidth = true;

            // 5. If a glyph has no SWIDTH/DWIDTH, inject the global defaults before BBX.
            if (inGlyph && inputLine.StartsWith("BBX ", StringComparison.Ordinal) && !glyphHasSwidth)
            {
                if (globalSwidth != null)
                    outputStream.WriteLine(globalSwidth);
                if (globalDwidth != null)
                    outputStream.WriteLine(globalDwidth);
            }

            // Rewrite font name properties if requested.
            if (fontName != null && RewriteFontNameLine(inputLine, fontName) is string rewritten)
            {
                outputStream.WriteLine(rewritten);
                continue;
            }

            outputStream.WriteLine(inputLine);
        }
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    return 0;
}

// ── Merge command implementation ──────────────────────────────────────────────

// Parses a BDF 2.1 file into its header lines and a dictionary of glyph line-blocks.
//
// headerLines receives every line from STARTFONT through CHARS N (inclusive).
// glyphs maps each glyph's ENCODING integer to its line-block (STARTCHAR…ENDCHAR).
//   Glyphs with ENCODING -1 (unmapped) get unique negative keys starting at -2 so
//   they are all retained without colliding.
// nextUnmappedKey is set to the next available unique negative key after all parsed glyphs.
// Returns false (and prints a diagnostic) when the file cannot be read or is not BDF 2.1.
bool TryParseBdf(string path, out List<string> headerLines, out Dictionary<int, List<string>> glyphs,
                 out int nextUnmappedKey)
{
    headerLines = [];
    glyphs = [];
    nextUnmappedKey = -2;
    string? line;
    try
    {
        using var reader = new StreamReader(path);

        // Validate that the first line declares BDF version 2.1.
        line = reader.ReadLine()?.TrimEnd();
        if (line == null || line != "STARTFONT 2.1")
        {
            Console.Error.WriteLine($"Error: '{path}' is not a BDF 2.1 file.");
            return false;
        }
        headerLines.Add(line);

        // Read header lines until the first STARTCHAR or ENDFONT.
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("STARTCHAR ", StringComparison.Ordinal) ||
                line.StartsWith("ENDFONT", StringComparison.Ordinal))
                break;
            headerLines.Add(line);
        }

        if (line == null || line.StartsWith("ENDFONT", StringComparison.Ordinal))
            return true;  // File has no glyph section.

        // Parse each glyph block (STARTCHAR … ENDCHAR).
        int unmappedKey = -2;  // Unique keys for ENCODING -1 glyphs.
        string? startCharLine = line;
        while (startCharLine != null)
        {
            var glyphLines = new List<string> { startCharLine };
            int encoding = -1;

            while ((line = reader.ReadLine()) != null)
            {
                glyphLines.Add(line);
                if (encoding == -1 && line.StartsWith("ENCODING ", StringComparison.Ordinal))
                {
                    string val = line["ENCODING ".Length..].Trim();
                    if (int.TryParse(val, out int enc))
                        encoding = enc;
                }
                if (line.StartsWith("ENDCHAR", StringComparison.Ordinal))
                    break;
            }

            // Assign a unique key: non-negative encodings use their value;
            // ENCODING -1 glyphs (unmapped) get a unique negative key.
            int key = (encoding == -1) ? unmappedKey-- : encoding;
            glyphs.TryAdd(key, glyphLines);

            // Advance to the next STARTCHAR or stop at ENDFONT / EOF.
            startCharLine = null;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("STARTCHAR ", StringComparison.Ordinal))
                {
                    startCharLine = line;
                    break;
                }
                if (line.StartsWith("ENDFONT", StringComparison.Ordinal))
                    break;
            }
        }
        nextUnmappedKey = unmappedKey;
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error reading '{path}': {ex.Message}");
        return false;
    }
    return true;
}

int MergeBdfFiles(string input1Path, string input2Path, string outputPath, string fontName)
{
    if (!TryParseBdf(input1Path, out var header1, out var glyphs1, out int nextUnmappedKey))
        return 1;
    if (!TryParseBdf(input2Path, out _, out var glyphs2, out _))
        return 1;

    // Merge glyphs: file1 takes precedence on encoding conflicts.
    // ENCODING -1 glyphs from file2 always get unique negative keys so they are retained.
    int unmappedKey2 = nextUnmappedKey - 1;
    var merged = new Dictionary<int, List<string>>(glyphs1);
    foreach (var (key, glyph) in glyphs2)
    {
        if (key < -1)
        {
            // Unmapped glyph from file2: assign a unique key to keep it.
            merged[unmappedKey2--] = glyph;
        }
        else
        {
            merged.TryAdd(key, glyph);
        }
    }

    try
    {
        using var writer = new StreamWriter(outputPath);

        // Write header from file1, replacing the CHARS count and font name properties.
        bool charsLineWritten = false;
        foreach (var hLine in header1)
        {
            if (hLine.StartsWith("CHARS ", StringComparison.Ordinal))
            {
                writer.WriteLine($"CHARS {merged.Count}");
                charsLineWritten = true;
            }
            else if (RewriteFontNameLine(hLine, fontName) is string rewritten)
            {
                writer.WriteLine(rewritten);
            }
            else
            {
                writer.WriteLine(hLine);
            }
        }
        // Guard: if the header had no CHARS line, emit one now.
        if (!charsLineWritten)
            writer.WriteLine($"CHARS {merged.Count}");

        // Write glyphs: mapped (encoding >= 0) in ascending order, unmapped last.
        // Partition once to avoid two passes over the dictionary.
        var mappedGlyphs = new List<KeyValuePair<int, List<string>>>();
        var unmappedGlyphs = new List<KeyValuePair<int, List<string>>>();
        foreach (var entry in merged)
        {
            if (entry.Key >= 0)
                mappedGlyphs.Add(entry);
            else
                unmappedGlyphs.Add(entry);
        }
        foreach (var glyphLines in mappedGlyphs.OrderBy(g => g.Key).Concat(unmappedGlyphs).Select(g => g.Value))
        {
            foreach (var gl in glyphLines)
                writer.WriteLine(gl);
        }

        writer.WriteLine("ENDFONT");
    }
    catch (IOException ex)
    {
        Console.Error.WriteLine($"Error writing '{outputPath}': {ex.Message}");
        return 1;
    }

    return 0;
}

// ── Shared helpers ────────────────────────────────────────────────────────────

// If line is a FAMILY_NAME, FONT_NAME, or FACE_NAME property, returns the line
// rewritten with fontName as the new value. Otherwise returns null.
string? RewriteFontNameLine(string line, string fontName)
{
    string escaped = fontName.Replace("\"", "\\\"");
    if (line.StartsWith("FAMILY_NAME ", StringComparison.Ordinal))
        return $"FAMILY_NAME \"{escaped}\"";
    if (line.StartsWith("FONT_NAME ", StringComparison.Ordinal))
        return $"FONT_NAME \"{escaped}\"";
    if (line.StartsWith("FACE_NAME ", StringComparison.Ordinal))
        return $"FACE_NAME \"{escaped}\"";
    return null;
}
