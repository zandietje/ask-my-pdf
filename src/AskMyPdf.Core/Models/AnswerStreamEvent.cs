namespace AskMyPdf.Core.Models;

public abstract record AnswerStreamEvent
{
    public record TextDelta(string Text) : AnswerStreamEvent;
    public record CitationReceived(Citation Citation) : AnswerStreamEvent;
    public record Done : AnswerStreamEvent;
}
