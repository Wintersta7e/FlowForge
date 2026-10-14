using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FlowForge.CLI;
using FlowForge.Core.Execution;
using FlowForge.UI;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// The README's "no network calls of its own" claim, checked against the compiled code rather than its text: no shipped
/// assembly references a <c>System.Net</c> assembly or a type in a <c>System.Net</c> namespace, however it is spelled.
/// A folder on a network share is reached through the file APIs, which this check does not (and cannot) cover.
/// </summary>
public class NoNetworkClaimTests
{
    private readonly ITestOutputHelper _output;

    public NoNetworkClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Shipped_assemblies_reference_no_network_api()
    {
        Assembly[] assemblies = [typeof(PipelineRunner).Assembly, typeof(CliExitCode).Assembly, typeof(App).Assembly];
        var mismatches = new List<string>();
        int scanned = 0;

        foreach (Assembly assembly in assemblies)
        {
            string file = Path.GetFileName(assembly.Location);
            using FileStream stream = File.OpenRead(assembly.Location);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();

            foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
            {
                string name = reader.GetString(reader.GetAssemblyReference(handle).Name);
                if (IsNetwork(name))
                {
                    mismatches.Add($"{file}: references assembly {name}");
                }
            }

            foreach (TypeReferenceHandle handle in reader.TypeReferences)
            {
                TypeReference type = reader.GetTypeReference(handle);
                string ns = reader.GetString(type.Namespace);
                if (IsNetwork(ns))
                {
                    mismatches.Add($"{file}: references type {ns}.{reader.GetString(type.Name)}");
                }
            }

            _output.WriteLine($"{file}: assembly-refs={reader.AssemblyReferences.Count} type-refs={reader.TypeReferences.Count}");
            scanned += reader.AssemblyReferences.Count + reader.TypeReferences.Count;
        }

        ClaimCheck.Report(_output, "no-network", scanned, assemblies.Length, mismatches);
    }

    private static bool IsNetwork(string name) =>
        string.Equals(name, "System.Net", StringComparison.Ordinal) || name.StartsWith("System.Net.", StringComparison.Ordinal);
}
