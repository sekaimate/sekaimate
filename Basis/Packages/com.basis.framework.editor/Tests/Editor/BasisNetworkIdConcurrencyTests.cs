using System.IO;
using NUnit.Framework;

public class BasisNetworkIdConcurrencyTests
{
    [TestCase("../Basis Server/BasisNetworkServer/BasisNetworkIDDatabase.cs")]
    [TestCase("Packages/com.basis.server/BasisNetworkServer/BasisNetworkIDDatabase.cs")]
    public void NetworkIdAssignmentSerializesLookupAndAllocation(string sourcePath)
    {
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains("private static readonly object AssignmentLock", source);
        StringAssert.Contains("lock (AssignmentLock)", source);
        StringAssert.Contains("assignedNewId = !UshortNetworkDatabase.TryGetValue", source);
        StringAssert.Contains("UshortNetworkDatabase[UniqueStringID] = value", source);
    }
}
