using System;
using System.IO;
using System.Security.Cryptography;
using DfsLib;

if (args.Length == 0 || args[0] == "help")
{
    PrintHelp();
    return;
}

var command = args[0];
if (string.IsNullOrEmpty(command))
{
    Console.Error.WriteLine("Please provide a command");
    return;
}

bool result = command switch
{
    "create" => CreateCommand(args),
    "extract" => ExtractCommand(args),
    "verify" => VerifyCommand(args),
    "list" => ListCommand(args),
    _ => throw new NotImplementedException($"Command {command} not implemented")
};

Environment.Exit(result ? 0 : 1);

static void PrintHelp()
{
    Console.WriteLine("Usage: dfs <command> [options]");
    Console.WriteLine("Commands:");
    Console.WriteLine("  create <inputDir> <outputFileName> [--crc]    Creates a DFS file from the specified directory with optional CRC.");
    Console.WriteLine("  extract <inputFile> <extractPath>             Extracts files from the specified DFS file to the specified path.");
    Console.WriteLine("  verify <inputFile>                            Verifies the integrity of the specified DFS file.");
    Console.WriteLine("  list <inputFile>                              Lists the contents of the specified DFS file.");
    Console.WriteLine("  help                                          Displays this help text.");
}

static bool CreateCommand(string[] args)
{
    string[] sectorAligned = { ".AUDIOPKG" };

    var inputDir = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrEmpty(inputDir))
    {
        Console.Error.WriteLine("Please provide a directory to create a DFS file from");
        return false;
    }
    if (!Directory.Exists(inputDir))
    {
        Console.Error.WriteLine($"Directory {inputDir} does not exist");
        return false;
    }
    var outputFileName = args.Length > 2 ? args[2] : null;
    if (string.IsNullOrEmpty(outputFileName))
    {
        Console.Error.WriteLine("Please provide a filename for the DFS file");
        return false;
    }

    bool enableCrc = args.Length > 3 && args[3] == "--crc";
    Console.WriteLine($"Creating DFS file {outputFileName} from {inputDir} with {(enableCrc ? "CRC" : "no CRC")}...");
    if (File.Exists(outputFileName))
    {
        File.Delete(outputFileName);
        if (Path.ChangeExtension(outputFileName, ".000") is string splitFile && File.Exists(splitFile))
        {
            File.Delete(splitFile);
        }
    }
    var sourceFiles = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories);
    using var writer = new DfsWriter(outputFileName, sourceFiles, sectorAligned, enableCrc: enableCrc);
    writer.Write();
    return true;
}

static bool ExtractCommand(string[] args)
{
    var input = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("Please provide a DFS file to read");
        return false;
    }
    var extractPath = args.Length > 2 ? args[2] : null;
    if (string.IsNullOrEmpty(extractPath))
    {
        Console.Error.WriteLine("Please provide a path to extract the files to");
        return false;
    }
    using var reader = new DfsReader(File.OpenRead(input));
    reader.Extract(extractPath);
    return true;
}

static bool VerifyCommand(string[] args)
{
    var input = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("Please provide a DFS file to read");
        return false;
    }
    using var reader = new DfsReader(File.OpenRead(input));
    try
    {
        reader.Verify();
        return true;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine(e.Message);
        return false;
    }
}

static bool ListCommand(string[] args)
{
    var input = args.Length > 1 ? args[1] : null;
    if (string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("Please provide a DFS file to read");
        return false;
    }
    using var reader = new DfsReader(File.OpenRead(input));
    foreach (var file in reader.EnumerateFiles())
    {
        Console.WriteLine(file);
    }
    return true;
}
