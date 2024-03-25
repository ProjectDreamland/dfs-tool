using Dfs;
const string input = "/Users/andrew/projects/dfs-tool/file-test/STRINGS.DFS";

using var reader = new DfsReader(File.OpenRead(input));

foreach (var file in reader.EnumerateFiles())
{
    Console.WriteLine(file);
}

reader.Verify();
reader.Extract("/Users/andrew/projects/dfs-tool/file-test");