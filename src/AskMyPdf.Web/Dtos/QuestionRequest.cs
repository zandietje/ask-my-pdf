namespace AskMyPdf.Web.Dtos;

public record QuestionRequest(string Question, string DocumentId, string? Engine = null);
