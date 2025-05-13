using Mykeels.CSharpRepl.Sample;

namespace Mykeels.CSharpRepl.Tests;

public class Introspector_ListComponentsTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ListComponents_ReturnsListOfComponents()
    {
        var components = Introspector.ListComponents(typeof(ScriptGlobals));
        Assert.That(components, Is.Not.Null);
        Assert.That(components, Has.Member("void Print(object obj)"));
        Assert.That(components, Has.Member("async System.Threading.Tasks.Task DownloadFile(string url, string path)"));
    }
}