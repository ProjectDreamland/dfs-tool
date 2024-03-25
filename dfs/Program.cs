// See https://aka.ms/new-console-template for more information
using System.Security.Cryptography;
using Dfs;

Console.WriteLine("Hello, World!");

const string input = "/Users/andrew/games/drive_c/Program Files (x86)/Midway Home Entertainment/AREA-51/AUDIO/MUSIC.DFS";
const string extractPath = "/Users/andrew/projects/dfs-tool/file-test";
const string roundtripPath = "/Users/andrew/projects/dfs-tool/roundtrip-test";
const string outputDfs = "/Users/andrew/projects/dfs-tool/dfs/MUSIC.DFS";

// Extract the original DFS file
using var reader = new DfsReader(File.OpenRead(input));

reader.Verify();

reader.Extract(extractPath);


// Write the extracted files to a new DFS file
string[] sectorAligned = [".AUDIOPKG"];
Console.WriteLine("Writing DFS file...");
if (File.Exists(outputDfs))
{
    File.Delete(outputDfs);
    if (Path.ChangeExtension(outputDfs, ".000") is string splitFile && File.Exists(splitFile))
    {
        File.Delete(splitFile);
    }
}
var sourceFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
using var writer = new DfsWriter(outputDfs, sourceFiles, sectorAligned);
writer.Write();

// Read the newly written DFS file

using var reader2 = new DfsReader(File.OpenRead(outputDfs));
Console.WriteLine("Reading DFS file...");

reader2.Verify();

reader2.Extract(roundtripPath);

var extractedFiles = Directory.GetFiles(roundtripPath, "*", SearchOption.AllDirectories);

// ensure they have the exact same file structure

if (sourceFiles.Length != extractedFiles.Length)
{
    Console.WriteLine("Mismatched number of files");
}

// Clean up the extracted files