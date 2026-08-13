using System;
using System.Threading.Tasks;
using NUnit.Framework;

public class BasisClipboardServiceTests
{
    [TestCase("https://example.test/player?basisClipboardE2E=1", true)]
    [TestCase("https://example.test/player?basisClipboardE2E=0", false)]
    [TestCase("https://example.test/player", false)]
    [TestCase("not a URL", false)]
    public void DevelopmentHarnessRequiresExplicitUrlOptIn(string absoluteUrl, bool expected)
    {
        Assert.That(BasisWebClipboardE2EConfiguration.IsEnabled(absoluteUrl), Is.EqualTo(expected));
    }

    [Test]
    public async Task WriteTextAsyncDelegatesExactUnicodeText()
    {
        var backend = new RecordingClipboardBackend();
        var service = new BasisClipboardService(backend);

        await service.WriteTextAsync("Basis日本語🦊");

        Assert.That(backend.WrittenText, Is.EqualTo("Basis日本語🦊"));
    }

    [Test]
    public async Task ReadTextAsyncReturnsBackendTextUnchanged()
    {
        var backend = new RecordingClipboardBackend
        {
            TextToRead = "改行を\n含むClipboard"
        };
        var service = new BasisClipboardService(backend);

        string text = await service.ReadTextAsync();

        Assert.That(text, Is.EqualTo("改行を\n含むClipboard"));
    }

    [Test]
    public void BackendPermissionFailureIsNotHidden()
    {
        var expected = new InvalidOperationException("clipboard-read denied");
        var backend = new RecordingClipboardBackend { ReadFailure = expected };
        var service = new BasisClipboardService(backend);

        InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ReadTextAsync());

        Assert.That(actual, Is.SameAs(expected));
    }

    private sealed class RecordingClipboardBackend : IBasisClipboardBackend
    {
        public string WrittenText { get; private set; }
        public string TextToRead { get; set; }
        public Exception ReadFailure { get; set; }

        public Task WriteTextAsync(string text)
        {
            WrittenText = text;
            return Task.CompletedTask;
        }

        public Task<string> ReadTextAsync()
        {
            if (ReadFailure != null)
            {
                return Task.FromException<string>(ReadFailure);
            }

            return Task.FromResult(TextToRead);
        }
    }
}
