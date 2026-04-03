using System;
using System.Linq;
using System.Reflection;

var assembly = typeof(Anthropic.AnthropicClient).Assembly;

// 1. AnthropicClient - constructor and key properties
Console.WriteLine("=== AnthropicClient ===");
var clientType = typeof(Anthropic.AnthropicClient);
foreach (var c in clientType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
{
    var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"  ctor({parms})");
}
foreach (var p in clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    Console.WriteLine($"  prop: {p.PropertyType.Name} {p.Name}");
}
foreach (var m in clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
{
    if (!m.IsSpecialName)
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  method: {m.ReturnType.Name} {m.Name}({parms})");
    }
}

// 2. IMessageService
Console.WriteLine("\n=== IMessageService ===");
var msgSvc = assembly.GetType("Anthropic.Services.IMessageService");
if (msgSvc != null)
{
    foreach (var m in msgSvc.GetMethods())
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({parms})");
    }
}

// 3. MessageCreateParams
Console.WriteLine("\n=== MessageCreateParams ===");
var mcp = assembly.GetType("Anthropic.Models.Messages.MessageCreateParams");
if (mcp != null)
{
    foreach (var p in mcp.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    foreach (var c in mcp.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
    {
        var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  ctor({parms})");
    }
}

// 4. DocumentBlockParam
Console.WriteLine("\n=== DocumentBlockParam ===");
var dbp = assembly.GetType("Anthropic.Models.Messages.DocumentBlockParam");
if (dbp != null)
{
    foreach (var p in dbp.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    foreach (var c in dbp.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
    {
        var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  ctor({parms})");
    }
}

// 5. Base64PdfSource
Console.WriteLine("\n=== Base64PdfSource ===");
var b64 = assembly.GetType("Anthropic.Models.Messages.Base64PdfSource");
if (b64 != null)
{
    foreach (var p in b64.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    foreach (var c in b64.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
    {
        var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  ctor({parms})");
    }
}

// 6. CacheControlEphemeral
Console.WriteLine("\n=== CacheControlEphemeral ===");
var cce = assembly.GetType("Anthropic.Models.Messages.CacheControlEphemeral");
if (cce != null)
{
    foreach (var p in cce.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    foreach (var c in cce.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
    {
        var parms = string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  ctor({parms})");
    }
}

// 7. CitationPageLocation
Console.WriteLine("\n=== CitationPageLocation ===");
var cpl = assembly.GetType("Anthropic.Models.Messages.CitationPageLocation");
if (cpl != null)
{
    foreach (var p in cpl.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}

// 8. TextDelta
Console.WriteLine("\n=== TextDelta ===");
var td = assembly.GetType("Anthropic.Models.Messages.TextDelta");
if (td != null)
{
    foreach (var p in td.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}

// 9. CitationsDelta
Console.WriteLine("\n=== CitationsDelta ===");
var cd = assembly.GetType("Anthropic.Models.Messages.CitationsDelta");
if (cd != null)
{
    foreach (var p in cd.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}

// 10. RawContentBlockDeltaEvent
Console.WriteLine("\n=== RawContentBlockDeltaEvent ===");
var rcbde = assembly.GetType("Anthropic.Models.Messages.RawContentBlockDeltaEvent");
if (rcbde != null)
{
    foreach (var p in rcbde.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
}

// 11. RawMessageStreamEvent
Console.WriteLine("\n=== RawMessageStreamEvent ===");
var rmse = assembly.GetType("Anthropic.Models.Messages.RawMessageStreamEvent");
if (rmse != null)
{
    foreach (var p in rmse.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
    foreach (var m in rmse.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        if (!m.IsSpecialName)
        {
            var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"  method: {m.ReturnType.Name} {m.Name}({parms})");
        }
    }
}

// 12. ContentBlockParam (how to convert DocumentBlockParam to ContentBlockParam)
Console.WriteLine("\n=== ContentBlockParam ===");
var cbp = assembly.GetType("Anthropic.Models.Messages.ContentBlockParam");
if (cbp != null)
{
    foreach (var m in cbp.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  static: {m.ReturnType.Name} {m.Name}({parms})");
    }
    // Check for implicit operators
    foreach (var m in cbp.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.Contains("op_Implicit") || m.Name.Contains("op_Explicit")))
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  operator: {m.ReturnType.Name} {m.Name}({parms})");
    }
}

// 13. MessageCreateParamsSystem
Console.WriteLine("\n=== MessageCreateParamsSystem ===");
var mcps = assembly.GetType("Anthropic.Models.Messages.MessageCreateParamsSystem");
if (mcps != null)
{
    foreach (var m in mcps.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  static: {m.ReturnType.Name} {m.Name}({parms})");
    }
}
