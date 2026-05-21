using System.Text;
using AutoNate.Web.Services.Content;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class ContentTypeSnifferTests
{
    [Fact]
    public void Sniff_Png_ReturnsImagePng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        Assert.Equal("image/png", ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_JpegSoiAndApp0Marker_ReturnsImageJpeg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.Equal("image/jpeg", ContentTypeSniffer.Sniff(bytes));
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Sniff_Gif_ReturnsImageGif(string header)
    {
        Assert.Equal("image/gif", ContentTypeSniffer.Sniff(Encoding.ASCII.GetBytes(header)));
    }

    [Fact]
    public void Sniff_RiffWebp_ReturnsImageWebp()
    {
        // "RIFF" + 4-byte size + "WEBP".
        var bytes = new byte[]
        {
            0x52, 0x49, 0x46, 0x46,
            0x00, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50
        };
        Assert.Equal("image/webp", ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_RiffWithoutWebp_ReturnsNull()
    {
        // Bare RIFF container (e.g. WAV) — should not be accepted as image/webp.
        var bytes = new byte[]
        {
            0x52, 0x49, 0x46, 0x46,
            0x00, 0x00, 0x00, 0x00,
            0x57, 0x41, 0x56, 0x45
        };
        Assert.Null(ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_Pdf_ReturnsApplicationPdf()
    {
        Assert.Equal("application/pdf",
            ContentTypeSniffer.Sniff(Encoding.ASCII.GetBytes("%PDF-1.4")));
    }

    [Fact]
    public void Sniff_ZipLocalFileHeader_ReturnsApplicationZip()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 };
        Assert.Equal("application/zip", ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_OleCompoundDocument_ReturnsApplicationXOleStorage()
    {
        var bytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        Assert.Equal("application/x-ole-storage", ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_HtmlPayload_ReturnsNull()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<html><body><script>alert(1)</script></body></html>");
        Assert.Null(ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_SvgWithScript_ReturnsNull()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\">" +
            "<script>alert(1)</script></svg>");
        Assert.Null(ContentTypeSniffer.Sniff(bytes));
    }

    [Fact]
    public void Sniff_PlainText_ReturnsNull()
    {
        Assert.Null(ContentTypeSniffer.Sniff(Encoding.UTF8.GetBytes("Hello, world!")));
    }

    [Fact]
    public void Sniff_ShortOrEmpty_ReturnsNull()
    {
        Assert.Null(ContentTypeSniffer.Sniff(Array.Empty<byte>()));
        Assert.Null(ContentTypeSniffer.Sniff(new byte[] { 0x89 }));
        Assert.Null(ContentTypeSniffer.Sniff(new byte[] { 0xFF, 0xD8 }));
    }

    [Theory]
    [InlineData("image/png", "image/png", true)]
    [InlineData("image/png", "image/jpeg", false)]
    [InlineData("image/png", "text/html", false)]
    [InlineData("image/png", "image/svg+xml", false)]
    [InlineData("image/png", "IMAGE/PNG", true)] // case-insensitive
    [InlineData("application/zip", "application/zip", true)]
    [InlineData("application/zip", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("application/zip", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true)]
    [InlineData("application/zip", "application/vnd.oasis.opendocument.text", true)]
    [InlineData("application/zip", "text/html", false)]
    [InlineData("application/zip", "image/png", false)]
    [InlineData("application/x-ole-storage", "application/msword", true)]
    [InlineData("application/x-ole-storage", "application/vnd.ms-excel", true)]
    [InlineData("application/x-ole-storage", "application/vnd.ms-powerpoint", true)]
    [InlineData("application/x-ole-storage", "application/zip", false)]
    [InlineData("application/pdf", "application/pdf", true)]
    [InlineData("application/pdf", "application/zip", false)]
    public void ClientTypeMatchesSniff_Cases(string sniffed, string claimed, bool expected)
    {
        Assert.Equal(expected, ContentTypeSniffer.ClientTypeMatchesSniff(sniffed, claimed));
    }

    [Fact]
    public void ClientTypeMatchesSniff_NullOrBlankClaim_ReturnsFalse()
    {
        Assert.False(ContentTypeSniffer.ClientTypeMatchesSniff("image/png", null));
        Assert.False(ContentTypeSniffer.ClientTypeMatchesSniff("image/png", ""));
        Assert.False(ContentTypeSniffer.ClientTypeMatchesSniff("image/png", "   "));
    }

    [Fact]
    public void ClientTypeMatchesSniff_UnknownCanonical_ReturnsFalse()
    {
        Assert.False(ContentTypeSniffer.ClientTypeMatchesSniff("text/csv", "text/csv"));
    }
}
