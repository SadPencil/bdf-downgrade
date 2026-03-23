// BDF font file downgrade tool: converts BDF version 2.2 to version 2.1.
//
// Key differences handled:
//   1. STARTFONT version line changed from 2.2 to 2.1
//   2. METRICSSET keyword (BDF 2.2-only) is removed
//   3. Global SWIDTH/DWIDTH/SWIDTH1/DWIDTH1/VVECTOR lines in the header are removed
//   4. Per-glyph SWIDTH1/DWIDTH1/VVECTOR lines (vertical metrics, BDF 2.2-only) are removed
//   5. Glyphs that relied on global SWIDTH/DWIDTH defaults get explicit SWIDTH/DWIDTH
//      lines inserted before their BBX line
//   6. STARTCHAR glyph names are replaced with 4-digit uppercase hex of the ENCODING value
//      (e.g. "STARTCHAR space" + "ENCODING 32" becomes "STARTCHAR 0020")

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: BdfDowngrade <input.bdf> <output.bdf>");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];

// First pass: collect the global SWIDTH/DWIDTH defaults from the file header
// (lines before STARTPROPERTIES that set defaults for glyphs missing their own).
string? globalSwidth = null;
string? globalDwidth = null;

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
bool pendingStartChar = false;  // STARTCHAR has been seen; waiting for ENCODING to emit it

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
        // 6. Buffer STARTCHAR; the name will be replaced with the hex ENCODING value.
        pendingStartChar = true;
        continue;
    }
    else if (inputLine.StartsWith("ENDCHAR", StringComparison.Ordinal))
    {
        inGlyph = false;
    }

    // 6 (continued). Emit the buffered STARTCHAR using the ENCODING value as a 4-digit hex name.
    if (pendingStartChar && inputLine.StartsWith("ENCODING ", StringComparison.Ordinal))
    {
        string encodingValue = inputLine.Substring("ENCODING ".Length).Trim();
        if (encodingValue.Length > 0 && int.TryParse(encodingValue, out int codePoint))
            outputStream.WriteLine($"STARTCHAR {codePoint:X4}");
        else
        {
            Console.Error.WriteLine($"Warning: could not parse ENCODING value '{encodingValue}'; STARTCHAR name left as-is.");
            outputStream.WriteLine($"STARTCHAR {encodingValue}");
        }
        pendingStartChar = false;
        // Fall through to also write the ENCODING line.
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

    outputStream.WriteLine(inputLine);
}

return 0;
