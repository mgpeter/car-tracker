using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// What may be attached, and what happens to the ones that may not.
/// </summary>
public sealed class ChatFilesTests
{
    private static ChatFile File(string mediaType, int bytes = 16) =>
        new(mediaType, Convert.ToBase64String(new byte[bytes]));

    private static List<ChatMessage> Transcript() => [new(ChatRole.User, "What is this?")];

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    public void The_four_readable_types_are_attached_to_the_message_they_came_with(string mediaType)
    {
        var transcript = Transcript();

        Assert.Empty(ChatFiles.Attach(transcript, [File(mediaType)]));

        var attachment = Assert.Single(transcript[0].Contents.OfType<DataContent>());
        Assert.Equal(mediaType, attachment.MediaType);
    }

    [Fact]
    public void A_type_the_model_cannot_read_is_refused_by_name()
    {
        // HEIC is the interesting one: DocumentStore accepts it happily, because storing bytes does not require
        // understanding them. The browser converts before it gets here, which is why a phone can attach one and
        // this list still cannot.
        var errors = ChatFiles.Attach(Transcript(), [File("image/heic")]);

        Assert.Contains("heic", errors["files[0]"][0]);
    }

    [Fact]
    public void More_than_five_says_how_many_there_were()
    {
        var errors = ChatFiles.Attach(Transcript(), [.. Enumerable.Repeat(File("image/jpeg"), 6)]);

        Assert.Contains("6", errors["files"][0]);
    }

    [Fact]
    public void An_oversize_file_says_how_big_it_was()
    {
        // "Too big" without a number is not actionable — the owner cannot tell whether to crop it or to give up.
        var errors = ChatFiles.Attach(Transcript(), [File("image/jpeg", ChatFiles.MaxBytesPerFile + 1)]);

        Assert.Contains("MB", errors["files[0]"][0]);
    }

    [Fact]
    public void One_bad_file_means_none_are_sent()
    {
        // Otherwise the turn quietly reads three of five and answers confidently about paperwork it never saw.
        var transcript = Transcript();

        var errors = ChatFiles.Attach(transcript, [File("image/jpeg"), File("text/csv"), File("image/png")]);

        Assert.Single(errors);
        Assert.Empty(transcript[0].Contents.OfType<DataContent>());
    }

    [Fact]
    public void Corrupt_base64_is_a_field_error_rather_than_an_exception()
    {
        var errors = ChatFiles.Attach(Transcript(), [new ChatFile("image/jpeg", "not base64 at all!!")]);

        Assert.Single(errors);
    }

    [Fact]
    public void A_message_with_no_files_is_left_exactly_as_it_was()
    {
        var transcript = Transcript();

        Assert.Empty(ChatFiles.Attach(transcript, null));
        Assert.Empty(ChatFiles.Attach(transcript, []));
        Assert.Single(transcript[0].Contents);
    }
}
