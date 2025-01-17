﻿/*
 * We wanted to create this tool to raise awareness of the problem, 
 * it will not be stable for programs with Arabic alphabet and could cause false positives with some emoticons.
 * Please note that this tool is an indicative template but you should not rely on it, it is really simple and basic.
 * 
 * Check https://dotnetsafer.com to know about us.
 * 
 */

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using TrojanSourceDetector;

const string wheel = @"|/-\";
ulong index = 0;

var isVerbose = args.Contains("-verbose") || args.Contains("-Verbose") || args.Contains("-v") || args.Contains("-V");
var isBom = args.Contains("-bom") || args.Contains("-BOM") || args.Contains("-b") || args.Contains("-B");
var isEsc = args.Contains("-esc") || args.Contains("-ESC") || args.Contains("-e") || args.Contains("-E");
var isWhitelist = args.Contains("-whitelist") || args.Contains("-Whitelist") || args.Contains("-w") || args.Contains("-W");

var originalColor = Console.ForegroundColor;
var defaultColor = ConsoleColor.White;
Console.ForegroundColor = ConsoleColor.DarkCyan;

Console.WriteLine(@"
______      _              _              __          
|  _  \    | |            | |            / _|         
| | | |___ | |_ _ __   ___| |_ ___  __ _| |_ ___ _ __ 
| | | / _ \| __| '_ \ / _ \ __/ __|/ _` |  _/ _ \ '__|
| |/ / (_) | |_| | | |  __/ |_\__ \ (_| | ||  __/ |   
|___/ \___/ \__|_| |_|\___|\__|___/\__,_|_| \___|_|   
                                                      
Hidden value checker to prevent source trojan vulnerability.

###############################################################
");

Console.ForegroundColor = defaultColor;

Console.WriteLine(@"
Based on the examples of: 
https://github.com/nickboucher/trojan-source/tree/main/C%23

and based on the article by Nicholas Boucher and Ross Anderson; 
https://www.trojansource.codes

This small tool will help you to verify if any file in your source code
contains any hidden characters that could be linked to this vulnerability.

######

To understand the why of the tool, check:

https://medium.com/@juanal98/trojan-source-in-dotnet-25f6ce190c1a
");

var nonRenderingCategories = new UnicodeCategory[] {
UnicodeCategory.Control,
UnicodeCategory.OtherNotAssigned,
UnicodeCategory.Format,
UnicodeCategory.Surrogate };


void alert(string text, int line)
{
    var defaultForegroundColor = ConsoleColor.White;
    var defaultBackgroundColor = Console.BackgroundColor;
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.Write("[Warning]: ");
    Console.ForegroundColor = defaultForegroundColor;
    Console.Write($"Hidden characters have been detected at line {line} in: ");
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.Write(text);
    Console.ForegroundColor = defaultForegroundColor;
    Console.WriteLine();

    if (isVerbose)
    {
        var start = Math.Max(0, line);
        var sourceLines = File.ReadAllLines(text).Skip(start).Take(1).ToArray();

        for (int i = 0; i < sourceLines.Length; ++i)
        {
            var sourceLine = sourceLines[i];
            Console.WriteLine($"appears as: [{start + i}] {sourceLine}");

            Console.Write($"actual    : [{start + i}] ");
            foreach (var c in sourceLine)
            {
                var (isPrintable, slug) = CharConverter(c);

                if (!isPrintable)
                {
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                }

                Console.Write(slug);

                if (!isPrintable)
                {
                    Console.BackgroundColor = defaultBackgroundColor;
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine();
    }

    static (bool isPrintable, string slug) CharConverter(char c)
    {
        var nonRenderingCategories = new UnicodeCategory[] {
        UnicodeCategory.Control,
        UnicodeCategory.OtherNotAssigned,
        UnicodeCategory.Format,
        UnicodeCategory.Surrogate };

        var category = Char.GetUnicodeCategory(c);

        var isPrintable = Char.IsWhiteSpace(c) ||
                !nonRenderingCategories.Contains(category);

        if (isPrintable) return (true, $"{c}");

        return (false, $"\\u{(ushort)c:X}");
    }

}

string? GetPathFromUser()
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("Please enter a directory with one or more .NET projects to start (Full dir path): ");
    Console.ForegroundColor = defaultColor;

    return Console.ReadLine();
}

string? path = null;

while (path is null or "")
{
    if (args.Length == 0 || !Directory.Exists(args[0]))
    {
        path = GetPathFromUser();

        if (path == string.Empty)
        {
            Environment.Exit(0);
            return;
        }
    }
    else
    {
        path = args[0];
    }
}

Console.WriteLine();

List<EmojiRecord> whiteList = new();

if (isWhitelist)
{
    Console.WriteLine("Using Emoji Whitelist.");
    var jsonPath = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "emojis.json");

    whiteList = JsonSerializer.Deserialize<List<EmojiRecord>>(File.ReadAllText(jsonPath));
    Console.WriteLine($"Emoji Whitelist contains {whiteList.Count} definitions.");
}


if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
{
    Console.ForegroundColor = ConsoleColor.DarkRed;
    Console.WriteLine("Invalid path, check your .NET project directory");
    Console.ForegroundColor = defaultColor;
    return;
}

string[] dotnetFiles = Directory.GetFiles(path: path, "*.*", SearchOption.AllDirectories);

// Filter to common C# Project Files
var regex = new Regex(@".*\.(cs|cshtml|aspx|config|razor|xaml|csproj|resx|ettings\.[^\.]+\.json)$");

var issuesCount = 0;

var scannedFiles = 0;
var problemFiles = 0;

var problematicFilesList = new List<(string filename, List<int> lines)>();
var dotnetFilesCount = dotnetFiles.Count();
Console.WriteLine();
foreach (var dotnetFile in dotnetFiles)
{
    if (regex.IsMatch(dotnetFile))
    {
        if (isVerbose)
        {
            //Console.WriteLine($"\b{scannedFiles}/{dotnetFilesCount}: {scannedFiles / dotnetFilesCount:P2}");
            Tick(ConsoleColor.DarkGreen);
        }
        string? currentLine = null;

        var sourceLines = File.ReadAllLines(dotnetFile);
        var lines = new List<int>();
        int lastReportedLine = -1;
        scannedFiles++;
        int positionInLine = -1;

        using StreamReader sr = new StreamReader(dotnetFile);
        int count = 0, line = 0;
        while (sr.Peek() >= 0)
        {
            var c = (char)sr.Read();
            count++; positionInLine++;
            if (currentLine is null)
            {
                currentLine = sourceLines[line];
            }
            if (c == '\n')
            {
                line++;
                positionInLine = -1;
                if (line < sourceLines.Length)
                {
                    currentLine = sourceLines[line];
                }
                continue;
            }
            var category = Char.GetUnicodeCategory(c);

            var isPrintable = Char.IsWhiteSpace(c) ||
                  !nonRenderingCategories.Contains(category);

            // Filter out Byte-Order-Marks and ESC sequences.
            if ((ushort)c == 0xFEFF && isBom)
            {
                continue;
            }

            if ((ushort)c == 0x7f && isEsc)
            {
                continue;
            }

            if (!isPrintable)
            {
                if (isVerbose)
                {
                    Tick(ConsoleColor.DarkRed);
                }

                var isSurrogate = Char.GetUnicodeCategory(c) == UnicodeCategory.Surrogate;
                var nextIsSurrogate = Char.GetUnicodeCategory(currentLine[positionInLine + 1]) == UnicodeCategory.Surrogate;
                var isSafe = false || (isSurrogate && nextIsSurrogate);
                if (isWhitelist && isSurrogate)
                {
                    foreach (var emoji in whiteList)
                    {
                        var regexMatcher = new Regex(emoji.RegexPattern);

                        var matches = regexMatcher.Matches(currentLine).Cast<Match>().ToList();
                        var foundMatch = false;

                        foreach (var match in matches)
                        {
                            if (match.Index < positionInLine && match.Index + match.Length > positionInLine)
                            {
                                //if (isVerbose)
                                //{
                                //    Console.ForegroundColor = ConsoleColor.DarkGreen;
                                //    Console.WriteLine($"Found whitelisted emoji ({emoji.UnicodeParts}) on line {line} of {dotnetFile}, skipping.");
                                //    Console.ForegroundColor = defaultColor;
                                //}
                                foundMatch = true;
                                break;
                            }
                        }

                        isSafe = foundMatch;

                        if (foundMatch)
                        {
                            break;
                        }
                    }
                }

                if (!isWhitelist || (!isSafe && !nextIsSurrogate))
                {
                    issuesCount++;

                    if (lastReportedLine == -1)
                    {
                        problemFiles++;
                    }

                    if (lastReportedLine != line)
                    {
                        lines.Add(line);
                        lastReportedLine = line;
                        alert(dotnetFile, line);
                    }
                }
            }
        }

        sr.Close();

        sr.Dispose();

        if (lines.Count > 0)
        {
            problematicFilesList.Add((dotnetFile, lines));
        }
    }
}


if (issuesCount == 0)
{
    Console.ForegroundColor = ConsoleColor.DarkGreen;
    Console.WriteLine();
    Console.WriteLine($"Perfect! No problems have been detected in the analysis of {scannedFiles} files.");
    Console.ForegroundColor = originalColor;
    return;
}

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine();
Console.WriteLine($"{problemFiles} compromised files containing {issuesCount} issues have been detected based on a total of {scannedFiles} files scanned, please review.");
Console.ForegroundColor = defaultColor;

if (isVerbose)
{
    foreach (var (name, list) in problematicFilesList.OrderBy(t => t.filename))
    {
        var lines = $"{name}: [{string.Join(",", list)}]";
        Console.WriteLine(lines);
    }
}
Console.ForegroundColor = originalColor;

void Tick(ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.Write($"\b{wheel[(int)index % wheel.Length]}");
    Console.ForegroundColor = defaultColor;
    index++;
}