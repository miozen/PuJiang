using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using PuJiang.Domain;
using PuJiang.V1.Persistence;
using Xunit;

namespace PuJiang.Tests;

[CollectionDefinition("Controlled sample environment", DisableParallelization = true)]
public sealed class ControlledSampleCollection;

[Collection("Controlled sample environment")]
public sealed class ControlledFictionalSamplesTests
{
    [Fact]
    public async Task Reopen_reads_the_same_data_and_new_sample_does_not_overwrite_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT");
        Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var first = await ControlledFictionalSamples.OpenLatestOrCreateAsync();
            var extra = Guid.NewGuid();
            await using (var db = first.Database.OpenContext()) { db.Lineages.Add(new Lineage(extra, first.GenealogyId, "虚构二谱")); await db.SaveChangesAsync(); }
            var reopened = await ControlledFictionalSamples.OpenLatestOrCreateAsync();
            Assert.Equal(first.Database.DatabasePath, reopened.Database.DatabasePath);
            Assert.Equal(first.GenealogyId, reopened.GenealogyId);
            await using (var db = reopened.Database.OpenContext()) Assert.True(await db.Lineages.AnyAsync(x => x.Id == extra));
            var second = await ControlledFictionalSamples.CreateAsync();
            Assert.NotEqual(first.Database.DatabasePath, second.Database.DatabasePath);
            Assert.Equal(second.GenealogyId, (await ControlledFictionalSamples.OpenLatestOrCreateAsync()).GenealogyId);
            await using var original = first.Database.OpenContext(); Assert.True(await original.Lineages.AnyAsync(x => x.Id == extra));
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Missing_or_invalid_sample_is_not_replaced()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT"); Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var sample = await ControlledFictionalSamples.CreateAsync();
            var pointer = Path.Combine(root, "V1FictionalSamples", "latest.json"); var before = await File.ReadAllTextAsync(pointer);
            sample.Database.Dispose();
            File.Move(sample.Database.DatabasePath, sample.Database.DatabasePath + ".held");
            await Assert.ThrowsAsync<FileNotFoundException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.Equal(before, await File.ReadAllTextAsync(pointer)); Assert.False(File.Exists(sample.Database.DatabasePath));
            File.Move(sample.Database.DatabasePath + ".held", sample.Database.DatabasePath);
            await File.WriteAllTextAsync(pointer, "{\"Version\":99}");
            await Assert.ThrowsAsync<InvalidDataException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.True(File.Exists(sample.Database.DatabasePath));
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Active_sample_pins_database_and_directory_against_replacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT"); Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var sample = await ControlledFictionalSamples.CreateAsync();
            var directory = Path.Combine(root, "V1FictionalSamples");
            Assert.Throws<IOException>(() => File.Move(sample.Database.DatabasePath, sample.Database.DatabasePath + ".moved"));
            Assert.Throws<IOException>(() => Directory.Move(directory, directory + ".moved"));
            await using (var db = sample.Database.OpenContext()) Assert.True(await db.Genealogies.AnyAsync(x => x.Id == sample.GenealogyId));
            sample.Database.Dispose();
            File.Move(sample.Database.DatabasePath, sample.Database.DatabasePath + ".moved");
            File.Move(sample.Database.DatabasePath + ".moved", sample.Database.DatabasePath);
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Requires_explicit_absolute_nonproduction_root()
    {
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", "relative");
            await Assert.ThrowsAsync<InvalidOperationException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuJiang"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Corrupt_database_and_identity_mismatch_preserve_data_and_pointer()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT"); Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var sample = await ControlledFictionalSamples.CreateAsync();
            var pointer = Path.Combine(root, "V1FictionalSamples", "latest.json"); var originalPointer = await File.ReadAllBytesAsync(pointer);
            await using (var db = sample.Database.OpenContext())
                await db.Database.ExecuteSqlRawAsync("UPDATE FictionalSampleIdentity SET SampleId = 'invalid'");
            var mismatchHash = SHA256.HashData(await File.ReadAllBytesAsync(sample.Database.DatabasePath));
            await Assert.ThrowsAsync<InvalidDataException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.Equal(mismatchHash, SHA256.HashData(await File.ReadAllBytesAsync(sample.Database.DatabasePath)));
            Assert.Equal(originalPointer, await File.ReadAllBytesAsync(pointer));
            var corrupt = System.Text.Encoding.UTF8.GetBytes("fictional corrupt file");
            await File.WriteAllBytesAsync(sample.Database.DatabasePath, corrupt);
            await Assert.ThrowsAsync<SqliteException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(sample.Database.DatabasePath));
            Assert.Equal(originalPointer, await File.ReadAllBytesAsync(pointer));
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Junction_sample_directory_is_rejected_without_touching_target()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(Path.GetTempPath(), "PuJiang-junction-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Directory.CreateDirectory(target);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT"); Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
            start.ArgumentList.Add(Path.Combine(root, "V1FictionalSamples")); start.ArgumentList.Add(target);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动链接测试。");
            await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            await Assert.ThrowsAsync<IOException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }

    [Fact]
    public async Task Hard_linked_sample_is_rejected_and_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "PuJiang-controlled-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT"); Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", root);
        try
        {
            var sample = await ControlledFictionalSamples.CreateAsync();
            var hardLink = Path.Combine(root, "fictional-hardlink.sqlite");
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/H");
            start.ArgumentList.Add(hardLink); start.ArgumentList.Add(sample.Database.DatabasePath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动硬链接测试。");
            await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await process.StandardError.ReadToEndAsync());
            var before = SHA256.HashData(await File.ReadAllBytesAsync(sample.Database.DatabasePath));
            await Assert.ThrowsAsync<IOException>(() => ControlledFictionalSamples.OpenLatestOrCreateAsync());
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(sample.Database.DatabasePath)));
            Assert.True(File.Exists(hardLink)); sample.Database.Dispose();
        }
        finally { Environment.SetEnvironmentVariable("PUJIANG_TEST_DATA_ROOT", previous); }
    }
}
