namespace AskMyPdf.Core.Models;

public record WordBoundingBox(
    string Text,
    double Left,
    double Bottom,
    double Right,
    double Top);
