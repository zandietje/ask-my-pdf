namespace AskMyPdf.Web.Endpoints;

using System.Text.Json;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Dtos;

public static class QuestionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapQuestionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/questions", async (QuestionRequest req, QuestionService svc, HttpContext ctx, ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AskMyPdf.QuestionEndpoints");
            if (string.IsNullOrWhiteSpace(req.Question))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "Question is required" });
                return;
            }
            if (string.IsNullOrWhiteSpace(req.DocumentId))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "DocumentId is required" });
                return;
            }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var ct = ctx.RequestAborted;

            try
            {
                await foreach (var evt in svc.StreamAnswerAsync(req.Question, req.DocumentId).WithCancellation(ct))
                {
                    var (eventType, data) = evt switch
                    {
                        AnswerStreamEvent.TextDelta td => ("text-delta", JsonSerializer.Serialize(
                            new { text = td.Text }, JsonOptions)),
                        AnswerStreamEvent.CitationReceived cr => ("citation", JsonSerializer.Serialize(
                            new
                            {
                                documentId = cr.Citation.DocumentId,
                                documentName = cr.Citation.DocumentName,
                                pageNumber = cr.Citation.PageNumber,
                                citedText = cr.Citation.CitedText,
                                highlightAreas = cr.Citation.HighlightAreas.Select(a => new
                                {
                                    pageIndex = a.PageIndex,
                                    left = a.Left,
                                    top = a.Top,
                                    width = a.Width,
                                    height = a.Height,
                                }),
                            }, JsonOptions)),
                        AnswerStreamEvent.Done => ("done", "{}"),
                        _ => ("unknown", "{}"),
                    };

                    await ctx.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — expected, no action needed
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error streaming answer for document {DocumentId}", req.DocumentId);
                var errorMsg = ex.Message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase)
                    ? "Rate limit reached. Please wait a moment before asking another question."
                    : "An error occurred while generating the answer. Please try again.";
                try
                {
                    await ctx.Response.WriteAsync(
                        $"event: error\ndata: {{\"error\":\"{errorMsg}\"}}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
                catch
                {
                    // Client may have disconnected
                }
            }
        });
    }
}
